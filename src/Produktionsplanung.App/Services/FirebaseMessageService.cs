using System.Net.Http;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
namespace Produktionsplanung.App.Services;

internal sealed class FirebaseMessageDto
{
    public string Id { get; set; }=""; public int SenderUserId { get; set; } public int RecipientUserId { get; set; }
    public string SenderUsernameSnapshot { get; set; }="";public string SenderDisplayNameSnapshot { get; set; }="";public string RecipientUsernameSnapshot { get; set; }="";public string RecipientDisplayNameSnapshot { get; set; }="";
    public string Subject { get; set; }="";public string Body { get; set; }="";public string Priority { get; set; }="Normal";
    public DateTime CreatedAtUtc { get; set; } public DateTime? AcknowledgedAtUtc { get; set; } public string? AcknowledgedByUsername { get; set; }
}
internal sealed class FirebaseMessagePage { public List<FirebaseMessageDto> Messages { get; set; }=new();public long Sequence { get; set; }public bool HasMore { get; set; } }
public static class FirebaseMessageService
{
    private static readonly SemaphoreSlim Gate=new(1,1);private static DateTime refreshed;
    public static bool IsPending(int id) { using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);return Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudMessageOutbox WHERE LocalId=@id AND Action='send' AND LastError=''",("@id",id)))>0; }
    public static string DeliveryError(int id) { using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);return Convert.ToString(FirebaseCloudStorage.Sql(db,"SELECT LastError FROM CloudMessageOutbox WHERE LocalId=@id LIMIT 1",("@id",id)))??""; }
    public static async Task<UserMessage> SendAsync(int recipientId,string subject,string body,string priority,CancellationToken token=default)
    {
        var user=SessionService.CurrentUser??throw new InvalidOperationException("Anmeldung erforderlich.");
        if(!FirebaseCompanyClient.OfflineAccessAllowed)throw new InvalidOperationException("Bitte erneut mit dem Firmenkonto anmelden.");
        using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
        var recipient=db.UserAccounts.AsNoTracking().SingleOrDefault(u=>u.Id==recipientId&&u.IsActive)??throw new InvalidOperationException("Empfänger ist nicht aktiv.");
        if(recipientId==user.Id)throw new InvalidOperationException("Ein Hinweis kann nicht an das eigene Konto gesendet werden.");
        var operationId=Guid.NewGuid().ToString("N");
        var message=new UserMessage { SenderUserId=user.Id,RecipientUserId=recipientId,SenderUsernameSnapshot=user.Username,SenderDisplayNameSnapshot=user.DisplayName,RecipientUsernameSnapshot=recipient.Username,RecipientDisplayNameSnapshot=recipient.DisplayName,Subject=subject,Body=body,Priority=priority };
        using(var tx=db.Database.BeginTransaction())
        {
            db.UserMessages.Add(message);db.SaveChanges();FirebaseCloudStorage.Sql(db,"INSERT INTO CloudMessageIds(LocalId,GlobalId) VALUES(@id,@guid)",("@id",message.Id),("@guid",operationId));
            FirebaseCloudStorage.Sql(db,"INSERT INTO CloudMessageOutbox(OperationId,UserId,Action,Payload,LocalId) VALUES(@op,@user,'send',@payload,@id)",("@op",operationId),("@user",user.Id),("@payload",JsonSerializer.Serialize(new { operationId,recipientUserId=recipientId,subject,body,priority },FirebaseCompanyClient.Json)),("@id",message.Id));tx.Commit();
        }
        try{await RefreshAsync(token,true).ConfigureAwait(false);}catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException){ }
        if(DeliveryError(message.Id) is { Length:>0 } error)throw new InvalidOperationException(error);
        return message;
    }
    public static async Task<bool> AcknowledgeAsync(int localId,CancellationToken token=default)
    {
        if(!FirebaseCompanyClient.OfflineAccessAllowed)throw new InvalidOperationException("Bitte erneut mit dem Firmenkonto anmelden.");
        using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
        var message=db.UserMessages.SingleOrDefault(m=>m.Id==localId&&m.RecipientUserId==SessionService.CurrentUser!.Id);if(message is null)return false;
        var id=Convert.ToString(FirebaseCloudStorage.Sql(db,"SELECT GlobalId FROM CloudMessageIds WHERE LocalId=@id",("@id",localId)))??"";
        if(id.Length!=32)throw new InvalidOperationException("Hinweis wurde noch nicht mit Firebase verbunden.");
        using(var tx=db.Database.BeginTransaction())
        {
            message.AcknowledgedAtUtc??=DateTime.UtcNow;message.AcknowledgedByUsername=SessionService.CurrentUser!.Username;db.SaveChanges();
            FirebaseCloudStorage.Sql(db,"INSERT OR IGNORE INTO CloudMessageOutbox(OperationId,UserId,Action,Payload,LocalId) VALUES(@op,@user,'ack',@payload,@id)",("@op",id),("@user",SessionService.CurrentUser.Id),("@payload",JsonSerializer.Serialize(new { messageId=id },FirebaseCompanyClient.Json)),("@id",localId));tx.Commit();
        }
        try{await RefreshAsync(token,true).ConfigureAwait(false);}catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException){ }
        if(DeliveryError(localId) is {Length:>0} error)throw new InvalidOperationException(error);
        return true;
    }
    public static async Task RefreshAsync(CancellationToken token=default,bool force=false)
    {
        if(!FirebaseCompanyClient.Enabled||!SessionService.IsAuthenticated)return;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if(!force&&DateTime.UtcNow-refreshed<TimeSpan.FromSeconds(5))return;
            using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
            foreach(var row in FirebaseCloudStorage.Query(db,"SELECT OperationId,Action,Payload,UserId FROM CloudMessageOutbox WHERE LastError='' ORDER BY rowid"))
            {
                if(Convert.ToInt32(row[3])!=SessionService.CurrentUser!.Id)continue;
                var payload=JsonSerializer.Deserialize<JsonElement>((string)row[2],FirebaseCompanyClient.Json);
                try
                {
                    if((string)row[1]=="send") { var result=await FirebaseCompanyClient.CallAsync<FirebaseMessageDto>("cloudMessageSend",payload,token).ConfigureAwait(false);Cache(db,result); }
                    else await FirebaseCompanyClient.CallAsync<object>("cloudMessageAcknowledge",payload,token).ConfigureAwait(false);
                    FirebaseCloudStorage.Sql(db,"DELETE FROM CloudMessageOutbox WHERE OperationId=@id",("@id",row[0]));
                }
                catch(FirebaseCloudException ex)when(ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
                {
                    using var tx=db.Database.BeginTransaction();
                    FirebaseCloudStorage.Sql(db,"UPDATE CloudMessageOutbox SET LastError=@error WHERE OperationId=@id",("@error",ex.Message),("@id",row[0]));
                    if((string)row[1]=="ack")FirebaseCloudStorage.Sql(db,"UPDATE UserMessages SET AcknowledgedAtUtc=NULL,AcknowledgedByUsername=NULL WHERE Id=(SELECT LocalId FROM CloudMessageOutbox WHERE OperationId=@id)",("@id",row[0]));
                    tx.Commit();
                }
            }
            var sequence=long.TryParse(FirebaseCloudStorage.Metadata(db,"messagesSequence"),out var value)?value:0;FirebaseMessagePage page;
            do
            {
                page=await FirebaseCompanyClient.CallAsync<FirebaseMessagePage>("cloudMessageList",new { after=sequence },token).ConfigureAwait(false);
                using var tx=db.Database.BeginTransaction();foreach(var message in page.Messages)Cache(db,message);sequence=page.Sequence;FirebaseCloudStorage.Metadata(db,"messagesSequence",sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));tx.Commit();
            }while(page.HasMore);
            refreshed=DateTime.UtcNow;
        }
        finally{Gate.Release();}
    }
    private static void Cache(AppDbContext db,FirebaseMessageDto m)
    {
        if(m.SenderUserId!=SessionService.CurrentUser!.Id&&m.RecipientUserId!=SessionService.CurrentUser.Id)throw new InvalidOperationException("Fremder Hinweis wurde abgewiesen.");
        var local=FirebaseCloudStorage.Sql(db,"SELECT LocalId FROM CloudMessageIds WHERE GlobalId=@id",("@id",m.Id));
        if(local is null)local=FirebaseCloudStorage.Sql(db,"SELECT CAST(LocalKey AS INTEGER) FROM CloudRowIds WHERE Kind='UserMessage' AND GlobalId=@id",("@id",m.Id));
        var id=local is not null?Convert.ToInt32(local):Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COALESCE(MAX(Id),0)+1 FROM UserMessages"));
        FirebaseCloudStorage.Sql(db,"INSERT INTO UserMessages(Id,SenderUserId,SenderUsernameSnapshot,SenderDisplayNameSnapshot,RecipientUserId,RecipientUsernameSnapshot,RecipientDisplayNameSnapshot,Subject,Body,Priority,CreatedAtUtc,AcknowledgedAtUtc,AcknowledgedByUsername) VALUES(@id,@sender,@sn,@sd,@recipient,@rn,@rd,@subject,@body,@priority,@created,@ack,@ackUser) ON CONFLICT(Id) DO UPDATE SET Subject=excluded.Subject,Body=excluded.Body,CreatedAtUtc=excluded.CreatedAtUtc,AcknowledgedAtUtc=COALESCE(excluded.AcknowledgedAtUtc,UserMessages.AcknowledgedAtUtc),AcknowledgedByUsername=COALESCE(excluded.AcknowledgedByUsername,UserMessages.AcknowledgedByUsername)",("@id",id),("@sender",m.SenderUserId),("@sn",m.SenderUsernameSnapshot),("@sd",m.SenderDisplayNameSnapshot),("@recipient",m.RecipientUserId),("@rn",m.RecipientUsernameSnapshot),("@rd",m.RecipientDisplayNameSnapshot),("@subject",m.Subject),("@body",m.Body),("@priority",m.Priority),("@created",m.CreatedAtUtc),("@ack",m.AcknowledgedAtUtc),("@ackUser",m.AcknowledgedByUsername));
        FirebaseCloudStorage.Sql(db,"INSERT OR IGNORE INTO CloudMessageIds(LocalId,GlobalId) VALUES(@local,@global)",("@local",id),("@global",m.Id));
    }
}

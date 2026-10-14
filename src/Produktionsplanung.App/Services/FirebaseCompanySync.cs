using System.Net.Http;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
namespace Produktionsplanung.App.Services;

internal sealed class CloudDataPage
{
    public string DataSetId { get; set; } = "";
    public string UserSetVersion { get; set; } = "legacy";
    public List<FirebaseCloudRecord> Records { get; set; } = new();
    public long Sequence { get; set; }
    public string? After { get; set; }
    public bool HasMore { get; set; }
    public List<FirebasePublicUser> Users { get; set; } = new();
    public DateTime LicenseValidUntilUtc { get; set; }
}
internal sealed class CloudApplyResult { public List<FirebaseCloudRecord> Records { get; set; }=new(); }
internal sealed record CloudImportResult(string DataSetId);
public static class FirebaseCompanySync
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    private static CancellationTokenSource? worker;
    public static string Status { get; private set; } = "Noch nicht synchronisiert";
    public static event Action? Changed;
    public static event Action? DataApplied;
    public static event Action? AccessRevoked;
    public static async Task ImportAsync(AppDbContext source,CancellationToken token=default)
    {
        var records=FirebaseCloudStorage.Export(source);
        var stage=await FirebaseCompanyClient.CallAsync<CloudImportResult>("cloudImportBegin",new { entryCount=records.Count },token);
        foreach(var page in CloudPacketizer.Pack(records,100)) { await FirebaseCompanyClient.CallAsync<object>("cloudImportPage",new { dataSetId=stage.DataSetId,records=page },token); Status=$"Firmendaten werden übertragen · {records.Count} Datensätze";Changed?.Invoke(); }
        await FirebaseCompanyClient.CallAsync<object>("cloudImportComplete",new { dataSetId=stage.DataSetId },token);
        FirebaseCompanyClient.Profile!.DataSetId=stage.DataSetId;
    }
    public static async Task ConnectAsync(string password,CancellationToken token=default)
    {
        var profile=FirebaseCompanyClient.Profile!;var destination=FirebaseCompanyClient.CachePath(profile);
        if(File.Exists(destination))
        {
            using var db=new AppDbContext(destination);FirebaseCloudStorage.EnsureTables(db);
            if(FirebaseCloudStorage.Metadata(db,"dataSetId")==profile.DataSetId)
            {
                ApplyUsers(db,profile.Users,profile,password);AppPaths.CloudDatabaseOverride=destination;
                AppSettingsService.Update(s=>{s.FirebaseCompanyEnabled=true;s.CompanyId=profile.CompanyId;s.CompanyCode=profile.CompanyCode;s.CompanyName=profile.CompanyName;s.SiteName=profile.SiteName;s.CompanyRegistrationMode="firebase";});
                SessionService.SignIn(db.UserAccounts.AsNoTracking().Single(u=>u.Id==profile.SourceUserId));FirebaseCompanyClient.PersistSession();return;
            }
            if(Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"))>0)throw new InvalidOperationException("Ein neuer Firmenstand ist verfügbar, aber lokale Änderungen sind noch ausstehend. Bitte diese zuerst klären.");
        }
        await BootstrapAsync(password,token);
    }
    public static async Task BootstrapAsync(string? password,CancellationToken token=default)
    {
        var profile=FirebaseCompanyClient.Profile ?? throw new InvalidOperationException("Firmenanmeldung erforderlich.");
        var destination=FirebaseCompanyClient.CachePath(profile);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary=destination+".download-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var db=new AppDbContext(temporary))
            {
                db.Database.EnsureCreated();DatabaseSchemaUpdater.Apply(db);FirebaseCloudStorage.EnsureTables(db);
                FirebaseCloudStorage.Sql(db,"PRAGMA foreign_keys=OFF");
                string? cursor=null;long startSequence=0;var first=true;int count=0;
                (string Hash,string Salt)? retainedCredential=null;
                if(password is null && File.Exists(destination))
                {
                    using var old=new AppDbContext(destination);FirebaseCloudStorage.EnsureTables(old);
                    if(FirebaseCloudStorage.Metadata(old,"credentialVersion")==profile.CredentialVersion)
                    {var oldUser=old.UserAccounts.AsNoTracking().SingleOrDefault(u=>u.Id==profile.SourceUserId);if(oldUser is not null)retainedCredential=(oldUser.PasswordHash,oldUser.PasswordSalt);}
                }
                do
                {
                    var page=await FirebaseCompanyClient.CallAsync<CloudDataPage>("cloudBootstrap",new { dataSetId=first?null:profile.DataSetId,after=cursor },token);
                    if(first) { startSequence=page.Sequence;profile.DataSetId=page.DataSetId;first=false; }
                    FirebaseCloudStorage.Apply(db,page.Records,false);ApplyUsers(db,page.Users,profile,password);if(page.Users.Count>0)profile.UserSetVersion=page.UserSetVersion;cursor=page.After;
                    count+=page.Records.Count;Status=$"Firmenkonto wird geladen · {count} Datensätze";Changed?.Invoke();
                } while(cursor is not null);
                long sequence=startSequence;CloudDataPage changes;
                do
                {
                    changes=await FirebaseCompanyClient.CallAsync<CloudDataPage>("cloudPullChanges",new { dataSetId=profile.DataSetId,after=sequence,userSetVersion=profile.UserSetVersion },token);
                    FirebaseCloudStorage.Apply(db,changes.Records,false);ApplyUsers(db,changes.Users,profile,password);if(changes.Users.Count>0)profile.UserSetVersion=changes.UserSetVersion;sequence=changes.Sequence;
                } while(changes.HasMore);
                if(retainedCredential is {} credential)FirebaseCloudStorage.Sql(db,"UPDATE UserAccounts SET PasswordHash=@hash,PasswordSalt=@salt WHERE Id=@id",("@hash",credential.Hash),("@salt",credential.Salt),("@id",profile.SourceUserId));
                FirebaseCloudStorage.Metadata(db,"credentialVersion",profile.CredentialVersion);
                FirebaseCloudStorage.Metadata(db,"dataSetId",profile.DataSetId!);FirebaseCloudStorage.Metadata(db,"sequence",sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if(FirebaseCloudStorage.Query(db,"PRAGMA foreign_key_check").Count>0)throw new InvalidOperationException("Der Download enthält unvollständige Datenbeziehungen. Bitte erneut versuchen.");
                FirebaseCloudStorage.Sql(db,"PRAGMA foreign_keys=ON");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(File.Exists(destination))File.Copy(destination,destination+".before-download",true);
            File.Move(temporary,destination,true);AppPaths.CloudDatabaseOverride=destination;
            AppSettingsService.Update(s=> { s.FirebaseCompanyEnabled=true;s.CompanyId=profile.CompanyId;s.CompanyCode=profile.CompanyCode;s.CompanyName=profile.CompanyName;s.SiteName=profile.SiteName;s.CompanyRegistrationMode="firebase";s.LicenseStatus="active";s.LicenseValidUntilUtc=profile.LicenseValidUntilUtc; });
            FirebaseCompanyClient.PersistSession();
            using var loginDb=new AppDbContext();SessionService.SignIn(loginDb.UserAccounts.Single(u=>u.Id==profile.SourceUserId));Status="Firmenkonto synchronisiert";
            if (System.Windows.Application.Current?.MainWindow is Produktionsplanung.App.MainWindow { IsLoaded: true } main)
            { if(main.Dispatcher.CheckAccess())main.ReloadFirebaseViews();else await main.Dispatcher.InvokeAsync(main.ReloadFirebaseViews); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal static void ApplyUsers(AppDbContext db,IEnumerable<FirebasePublicUser> users,FirebaseCompanyProfile profile,string? password=null)
    {
        var values=users.ToList();if(values.Count==0)return;
        var activeIds=string.Join(',',values.Select(u=>u.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        FirebaseCloudStorage.Sql(db,$"UPDATE UserAccounts SET IsActive=0 WHERE Id NOT IN ({activeIds})");
        foreach(var u in values)
        {
            var hash="";var salt="";
            if(u.Id==profile.SourceUserId && password is not null) (hash,salt)=PasswordService.HashPassword(password);
            FirebaseCloudStorage.Sql(db,"INSERT INTO UserAccounts(Id,Username,DisplayName,Role,IsActive,PasswordHash,PasswordSalt,CreatedAtUtc) VALUES(@id,@name,@display,@role,@active,@hash,@salt,@date) ON CONFLICT(Id) DO UPDATE SET Username=excluded.Username,DisplayName=excluded.DisplayName,Role=excluded.Role,IsActive=excluded.IsActive,PasswordHash=CASE WHEN @hash<>'' THEN @hash WHEN @id=@current THEN UserAccounts.PasswordHash ELSE '' END,PasswordSalt=CASE WHEN @salt<>'' THEN @salt WHEN @id=@current THEN UserAccounts.PasswordSalt ELSE '' END",("@id",u.Id),("@current",profile.SourceUserId),("@name",u.Username),("@display",u.DisplayName),("@role",u.Role),("@active",u.IsActive?1:0),("@hash",hash),("@salt",salt),("@date",DateTime.UtcNow));
        }
        if(password is not null)FirebaseCloudStorage.Metadata(db,"credentialVersion",profile.CredentialVersion);
    }
    public static void Start()
    {
        if(!FirebaseCompanyClient.Enabled||worker is not null)return;
        worker=new();var token=worker.Token;
        _=Task.Run(async()=> { while(!token.IsCancellationRequested) { try { await SynchronizeAsync(token); } catch(OperationCanceledException)when(token.IsCancellationRequested){break;} catch(Exception ex){Status=ex.Message;Changed?.Invoke();} try {await Task.Delay(TimeSpan.FromSeconds(15),token);}catch(OperationCanceledException){break;} } });
    }
    public static async Task PauseAsync() { Stop(); await Gate.WaitAsync().ConfigureAwait(false); Gate.Release(); }
    public static void Stop() { worker?.Cancel();worker?.Dispose();worker=null; }
    public static async Task SynchronizeAsync(CancellationToken token=default)
    {
        if(!FirebaseCompanyClient.Enabled||!SessionService.IsAuthenticated||FirebaseCompanyClient.Profile is not {} profile)return;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var db=new AppDbContext();FirebaseCloudStorage.EnsureTables(db);
            var pending=FirebaseCloudStorage.Pending(db).FirstOrDefault();
            if(pending is not null&&pending.UserId==profile.SourceUserId)
            {
                var records=System.Text.Json.JsonSerializer.Deserialize<List<FirebaseCloudRecord>>(pending.Payload,FirebaseCompanyClient.Json)!;
                try
                {
                    var result=await FirebaseCompanyClient.CallAsync<CloudApplyResult>("cloudApplyChanges",new { operationId=pending.OperationId,dataSetId=profile.DataSetId,records },token).ConfigureAwait(false);
                    using var tx=db.Database.BeginTransaction();
                    FirebaseCloudStorage.Sql(db,"DELETE FROM CloudOutbox WHERE Id=@id AND OperationId=@operation",("@id",pending.Id),("@operation",pending.OperationId));
                    // Edits queued during this HTTP request must survive and follow our acknowledged version.
                    foreach(var row in FirebaseCloudStorage.Query(db,"SELECT Id,Payload FROM CloudOutbox WHERE Id>@id",("@id",pending.Id)))
                    {
                        var next=System.Text.Json.JsonSerializer.Deserialize<List<FirebaseCloudRecord>>((string)row[1],FirebaseCompanyClient.Json)!;
                        foreach(var n in next)if(result.Records.FirstOrDefault(r=>r.Kind==n.Kind&&r.Id==n.Id&&r.BaseVersion==n.BaseVersion) is {} ack)n.BaseVersion=ack.Version;
                        FirebaseCloudStorage.Sql(db,"UPDATE CloudOutbox SET Payload=@payload WHERE Id=@id",("@payload",System.Text.Json.JsonSerializer.Serialize(next,FirebaseCompanyClient.Json)),("@id",row[0]));
                    }
                    FirebaseCloudStorage.Apply(db,result.Records);
                    foreach(var r in result.Records)FirebaseCloudStorage.Sql(db,"UPDATE CloudRowIds SET Version=@version WHERE Kind=@kind AND GlobalId=@guid",("@version",r.Version),("@kind",r.Kind),("@guid",r.Id));
                    tx.Commit();
                }
                catch(FirebaseCloudException ex)when(ex.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
                {
                    FirebaseCloudStorage.Sql(db,"UPDATE CloudOutbox SET Attempts=Attempts+1,LastError=@error,NextRetryAtUtc='9999-12-31' WHERE Id=@id",("@id",pending.Id),("@error",ex.Message));Status="Konflikt · "+ex.Message;Changed?.Invoke();return;
                }
                catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException)
                {
                    var retry=DateTime.UtcNow.AddSeconds(Math.Min(3600,30*Math.Pow(2,Math.Min(pending.Attempts,7))));
                    FirebaseCloudStorage.Sql(db,"UPDATE CloudOutbox SET Attempts=Attempts+1,LastError=@error,NextRetryAtUtc=@retry WHERE Id=@id",("@id",pending.Id),("@error","Verbindung unterbrochen"),("@retry",retry.ToString("O")));throw;
                }
            }
            var dispatcher=System.Windows.Application.Current?.Dispatcher;
            var canApply=dispatcher is null || (dispatcher.CheckAccess() ? !CloudEditBaseline.HasDrafts : await dispatcher.InvokeAsync(()=>!CloudEditBaseline.HasDrafts));
            if(!canApply) { Status="Neue Firmendaten werden nach dem Speichern geladen";Changed?.Invoke();return; }
            var seq=long.TryParse(FirebaseCloudStorage.Metadata(db,"sequence"),out var saved)?saved:0;CloudDataPage page;
            do
            {
                page=await FirebaseCompanyClient.CallAsync<CloudDataPage>("cloudPullChanges",new { dataSetId=profile.DataSetId,after=seq,userSetVersion=profile.UserSetVersion },token).ConfigureAwait(false);
                using var tx=db.Database.BeginTransaction();FirebaseCloudStorage.Sql(db,"PRAGMA defer_foreign_keys=ON");
                FirebaseCloudStorage.Apply(db,page.Records);ApplyUsers(db,page.Users,profile);if(page.Users.Count>0)profile.UserSetVersion=page.UserSetVersion;seq=page.Sequence;FirebaseCloudStorage.Metadata(db,"sequence",seq.ToString(System.Globalization.CultureInfo.InvariantCulture));tx.Commit();
                if(page.Records.Count>0)DataApplied?.Invoke();
                if(page.LicenseValidUntilUtc!=default)profile.LicenseValidUntilUtc=page.LicenseValidUntilUtc;
            }while(page.HasMore);
            profile.LastVerifiedAtUtc=DateTime.UtcNow;FirebaseCompanyClient.PersistSession();
            var remaining=Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"));var unresolved=FirebaseCloudStorage.Query(db,"SELECT LastError FROM CloudOutbox WHERE NextRetryAtUtc='9999-12-31' ORDER BY Id LIMIT 1");
            Status=unresolved.Count>0?"Konflikt · "+unresolved[0][0]:remaining>0?$"{remaining} Änderungspakete ausstehend":"Firmenkonto synchronisiert · "+DateTime.Now.ToString("HH:mm");Changed?.Invoke();
        }
        catch(FirebaseCloudException ex)when(ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            profile.LastVerifiedAtUtc=DateTime.MinValue;FirebaseCompanyClient.PersistSession();AccessRevoked?.Invoke();Status="Firmenkonto gesperrt oder Anmeldung abgelaufen";Changed?.Invoke();throw;
        }
        finally { Gate.Release(); }
    }
}

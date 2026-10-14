using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private sealed class CloudFixture : IDisposable
    {
        private readonly HttpClient previous = FirebaseCompanyClient.Http;
        public readonly List<FirebaseCloudRecord> Records;
        public readonly CloudFakeHandler Handler;
        public CloudFixture()
        {
            AppPaths.CloudDatabaseOverride = null;
            using var source = new AppDbContext();
            var employee = source.Employees.AsNoTracking().First(); var workstation = source.Workstations.AsNoTracking().First(); var shift = source.Shifts.AsNoTracking().First();
            source.PlanningAssignments.Add(new PlanningAssignment { EmployeeId=employee.Id, WorkstationId=workstation.Id, ShiftId=shift.Id, Date=new DateTime(2032,4,5), StartTime=shift.StartTime, EndTime=shift.EndTime, BreakMinutes=shift.BreakMinutes });
            source.SaveChanges();
            Records=JsonSerializer.Deserialize<List<FirebaseCloudRecord>>(JsonSerializer.Serialize(FirebaseCloudStorage.Export(source),FirebaseCompanyClient.Json),FirebaseCompanyClient.Json)!;
            foreach(var r in Records)r.Version=1;
            FirebaseCompanyClient.Profile=new FirebaseCompanyProfile { CompanyId=Guid.NewGuid().ToString("N"),CompanyCode="SYNTHETIC",CompanyName="Synthetic cloud company",SourceUserId=1,Username="homeoffice",DisplayName="Homeoffice test",Role=UserRoles.Planner,DataSetId=Guid.NewGuid().ToString("N"),LicenseValidUntilUtc=DateTime.UtcNow.AddDays(10),LastVerifiedAtUtc=DateTime.UtcNow,IdToken="synthetic-id-token",RefreshToken="synthetic-refresh-token",TokenExpiresAtUtc=DateTime.UtcNow.AddHours(1),Users=new(){new(1,"homeoffice","Homeoffice test",UserRoles.Planner,true),new(2,"administrator","Administrator",UserRoles.Administrator,true)} };
            LicenseService.EnsureLocalLicenseIdentity();
            Handler=new CloudFakeHandler(Records);FirebaseCompanyClient.Http=new HttpClient(Handler);
        }
        public void Connect()=>FirebaseCompanySync.BootstrapAsync("Synthetic-Password1").GetAwaiter().GetResult();
        public void Dispose()
        {
            FirebaseCompanySync.Stop();FirebaseCompanyClient.Http.Dispose();FirebaseCompanyClient.Http=previous;FirebaseCompanyClient.Profile=null;AppPaths.CloudDatabaseOverride=null;CloudEditBaseline.Clear();
        }
    }
    private sealed class CloudFakeHandler(List<FirebaseCloudRecord> records) : HttpMessageHandler
    {
        public bool FailApply;public bool IncompleteAck;public bool FailBootstrap;public Action? BeforeApply;private int pages;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Check(request.Headers.Authorization?.Parameter=="synthetic-id-token","Cloud request omitted authenticated user token");
            Check(request.Headers.Contains("X-SolutionCompakt-Device")&&request.Headers.Contains("X-SolutionCompakt-Secret"),"Cloud request omitted device proof");
            object result;
            var users=FirebaseCompanyClient.Profile!.Users;
            if(request.RequestUri!.AbsolutePath.EndsWith("cloudBootstrap"))
            {
                pages++;if(FailBootstrap&&pages%2==0)throw new HttpRequestException("Synthetic interrupted download");
                result=new {dataSetId=FirebaseCompanyClient.Profile.DataSetId,records,sequence=0,after=FailBootstrap?"Employee_"+new string('a',32):null,users};
            }
            else if(request.RequestUri.AbsolutePath.EndsWith("cloudPullChanges"))result=new {records=Array.Empty<FirebaseCloudRecord>(),sequence=0,hasMore=false,users,licenseValidUntilUtc=DateTime.UtcNow.AddDays(10)};
            else if(request.RequestUri.AbsolutePath.EndsWith("cloudApplyChanges"))
            {
                if(FailApply)throw new HttpRequestException("Synthetic offline connection");
                var payload=JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                var accepted=payload.GetProperty("records").Deserialize<List<FirebaseCloudRecord>>(FirebaseCompanyClient.Json)!;
                var callback=BeforeApply;BeforeApply=null;callback?.Invoke();
                foreach(var r in accepted)r.Version=r.BaseVersion+1;
                result=new {records=IncompleteAck?new List<FirebaseCloudRecord>():accepted};
            }
            else throw new InvalidOperationException("Unexpected cloud endpoint: "+request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result,FirebaseCompanyClient.Json),System.Text.Encoding.UTF8,"application/json")});
        }
    }
    private static void CloudHomeofficeBootstrap()
    {
        using var fixture=new CloudFixture();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var original=File.ReadAllBytes(AppPaths.LocalDatabasePath);fixture.Connect();
        Check(AppSettingsService.Load().CompanyId==FirebaseCompanyClient.Profile!.CompanyId,"Homeoffice installer created a different tenant");
        Check(SessionService.CurrentUser?.Username=="homeoffice","Cloud user was not logged in");
        using var db=new AppDbContext();
        Check(db.Employees.Count()==fixture.Records.Count(r=>r.Kind==nameof(Employee)),"Fresh installer omitted employees");
        Check(FirebaseCloudStorage.Query(db,"PRAGMA foreign_key_check").Count==0,"Bootstrap broke foreign keys");
        Check(db.UserAccounts.Single(u=>u.Id==2).PasswordHash=="","Another user's password verifier was distributed");
        Check(PasswordService.Verify("Synthetic-Password1",db.UserAccounts.Single(u=>u.Id==1).PasswordHash,db.UserAccounts.Single(u=>u.Id==1).PasswordSalt),"Current user offline credential was not created");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Check(original.SequenceEqual(File.ReadAllBytes(AppPaths.LocalDatabasePath)),"Cloud bootstrap modified the original local database");
    }
    private static void CloudIdentityRemapping()
    {
        using var fixture=new CloudFixture();var path=Path.Combine(AppPaths.DataDirectory,"remapped.db");
        using var db=new AppDbContext(path);db.Database.EnsureCreated();DatabaseSchemaUpdater.Apply(db);
        db.Employees.Add(new Employee{PersonnelNumber="UNRELATED-LOCAL",FirstName="Unrelated",LastName="Local"});db.SaveChanges();
        FirebaseCloudStorage.Sql(db,"PRAGMA foreign_keys=OFF");FirebaseCloudStorage.Apply(db,fixture.Records,false);
        var sourceEmployee=fixture.Records.First(r=>r.Kind==nameof(Employee));
        var mapped=FirebaseCloudStorage.Query(db,"SELECT LocalKey FROM CloudRowIds WHERE Kind='Employee' AND GlobalId=@id",("@id",sourceEmployee.Id)).Single()[0].ToString();
        Check(mapped!="1","Global identity reused a colliding local integer ID");
        Check(FirebaseCloudStorage.Query(db,"PRAGMA foreign_key_check").Count==0,"Remapped relational graph is inconsistent");
        var assignment=db.PlanningAssignments.AsNoTracking().Include(a=>a.Employee).First(a=>a.Date==new DateTime(2032,4,5));
        Check(assignment.Employee.PersonnelNumber!="UNRELATED-LOCAL","Foreign key pointed to an unrelated local employee");
    }
    private static void CloudOutboxSurvivesReconnectAndRollback()
    {
        using var fixture=new CloudFixture();fixture.Connect();
        using(var db=new AppDbContext())
        {
            using(var tx=db.Database.BeginTransaction()){db.Employees.First().LastName="Rolled back";db.SaveChanges();}
            Check(Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"))==0,"Rolled-back data left a cloud operation");
        }
        using(var db=new AppDbContext()){db.Employees.First().LastName="Offline edited";db.SaveChanges();}
        fixture.Handler.FailApply=true;
        try{FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();}catch(HttpRequestException){ }
        using(var db=new AppDbContext())Check(Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"))>0,"Connection failure erased pending edit");
        FirebaseCompanySync.ConnectAsync("Synthetic-Password1").GetAwaiter().GetResult();
        using(var db=new AppDbContext()) {Check(db.Employees.Any(e=>e.LastName=="Offline edited"),"Reconnect overwrote local draft");Check(Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"))>0,"Reconnect erased the persistent outbox");}
    }
    private static void CloudAcknowledgementPreservesFollowup()
    {
        using var fixture=new CloudFixture();fixture.Connect();
        using(var db=new AppDbContext()){db.Employees.First().LastName="First cloud edit";db.SaveChanges();}
        fixture.Handler.IncompleteAck=true;
        var rejected=false;try{FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
        using(var db=new AppDbContext())Check(rejected&&Convert.ToInt32(FirebaseCloudStorage.Sql(db,"SELECT COUNT(*) FROM CloudOutbox"))==1,"Incomplete server response erased a pending edit");
        fixture.Handler.IncompleteAck=false;
        fixture.Handler.BeforeApply=()=>{using var db=new AppDbContext();db.Employees.First().LastName="Followup during HTTP";db.SaveChanges();};
        FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();
        using(var db=new AppDbContext())
        {
            var queued=FirebaseCloudStorage.Query(db,"SELECT Payload FROM CloudOutbox");Check(queued.Count==1,"Acknowledgement dropped or duplicated the followup");
            var pending=JsonSerializer.Deserialize<List<FirebaseCloudRecord>>((string)queued[0][0],FirebaseCompanyClient.Json)!;
            Check(pending.Single().BaseVersion==2,"Followup did not advance to our acknowledged version");
            Check(db.Employees.Any(e=>e.LastName=="Followup during HTTP"),"Older server response overwrote newer local input");
        }
        FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();
        using var verify=new AppDbContext();Check(Convert.ToInt32(FirebaseCloudStorage.Sql(verify,"SELECT COUNT(*) FROM CloudOutbox"))==0,"Followup was not eventually acknowledged");
    }

    private static void CloudConflictRebasePreservesFollowup()
    {
        using var fixture=new CloudFixture();fixture.Connect();
        using(var db=new AppDbContext()){db.Employees.First().LastName="Conflicting edit";db.SaveChanges();}
        using(var db=new AppDbContext()){db.Employees.First().LastName="Later local edit";db.SaveChanges();}
        using(var db=new AppDbContext())
        {
            var rows=FirebaseCloudStorage.Query(db,"SELECT Id,OperationId,Payload FROM CloudOutbox ORDER BY Id");
            var id=Convert.ToInt64(rows[0][0]);var remote=JsonSerializer.Deserialize<List<FirebaseCloudRecord>>((string)rows[0][2],FirebaseCompanyClient.Json)!;
            remote.Single().Version=4;
            var rejected=false;try{FirebaseCompanySync.RebaseConflict(db,id,remote);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"A connection failure could create a new operation and duplicate an accepted request");
            FirebaseCloudStorage.Sql(db,"UPDATE CloudOutbox SET NextRetryAtUtc='9999-12-31',LastError='Synthetic conflict' WHERE Id=@id",("@id",id));
            FirebaseCompanySync.RebaseConflict(db,id,remote);
            var rebased=FirebaseCloudStorage.Query(db,"SELECT OperationId,Payload FROM CloudOutbox ORDER BY Id");
            Check((string)rebased[0][0]!=(string)rows[0][1]&&(string)rebased[1][0]==(string)rows[1][1],"Rebase changed the wrong operation identifiers");
            Check(rebased.All(r=>JsonSerializer.Deserialize<List<FirebaseCloudRecord>>((string)r[1],FirebaseCompanyClient.Json)!.Single().BaseVersion==4),"Rebase left later drafts on an obsolete server version");
        }
        FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();FirebaseCompanySync.SynchronizeAsync().GetAwaiter().GetResult();
        using var verify=new AppDbContext();Check(Convert.ToInt32(FirebaseCloudStorage.Sql(verify,"SELECT COUNT(*) FROM CloudOutbox"))==0&&verify.Employees.Any(e=>e.LastName=="Later local edit"),"Conflict resolution lost a later edit");
    }

    private static void CloudFailedBootstrapPreservesCache()
    {
        using var fixture=new CloudFixture();fixture.Connect();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();var path=AppPaths.DatabasePath;var original=File.ReadAllBytes(path);
        fixture.Handler.FailBootstrap=true;
        var failed=false;try{FirebaseCompanySync.BootstrapAsync("Synthetic-Password1").GetAwaiter().GetResult();}catch(HttpRequestException){failed=true;}
        Check(failed&&original.SequenceEqual(File.ReadAllBytes(path)),"Interrupted bootstrap replaced the working cache");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.download-*").Length==0,"Interrupted cache download was left active");
    }
}

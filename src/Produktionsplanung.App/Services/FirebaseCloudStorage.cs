using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public sealed class FirebaseCloudRecord
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public long BaseVersion { get; set; }
    public long Version { get; set; }
    public bool Deleted { get; set; }
    public JsonObject Data { get; set; } = new();
    public string UpdatedAtUtc { get; set; } = "";
}
internal sealed record CloudQueuedOperation(long Id,string OperationId,int UserId,string Payload,int Attempts,string Error);
internal sealed record CloudLocalIdentity(string Kind,string LocalKey,string GlobalId,long Version);

public static class FirebaseCloudStorage
{
    private static readonly JsonObject Schema = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cloud-schema.json")))!.AsObject();
    [ThreadStatic] internal static bool ApplyingRemote;
    public static IReadOnlyCollection<string> SupportedKinds => Schema.Select(x=>x.Key).ToArray();
    internal static string KindLabel(string kind)=>kind switch
    {
        "Employee"=>"Mitarbeiter","Qualification"=>"Qualifikation","EmployeeQualification"=>"Mitarbeiterqualifikation","Workstation"=>"Arbeitsplatz","Shift"=>"Schicht","WorkstationShiftRule"=>"Schichtregel","Absence"=>"Abwesenheit","OperatingCalendarDay"=>"Betriebskalender","WorkTimeEntry"=>"Arbeitszeit","PlanningAssignment"=>"Planung","ArticleMaster"=>"Artikel","ProductionOrder"=>"Produktionsauftrag","ProductionRunSlot"=>"Produktionsschicht","ProductionActual"=>"Ist-Produktion","DowntimeEntry"=>"Stillstand","OperationDefinition"=>"Arbeitsgang","ManufacturingRouting"=>"Arbeitsplan","RoutingStep"=>"Arbeitsplanschritt","JobCard"=>"Chargenkarte","RecycleBinItem"=>"Papierkorb","ShiftHandover"=>"Schichtübergabe",_=>kind
    };
    internal static string DisplayTitle(string kind,JsonObject data)
    {
        var title=string.Join(" ",new[]{"PersonnelNumber","FirstName","LastName","OrderNumber","ArticleNumber","Name","Subject"}.Select(k=>data[k]?.ToString()).Where(s=>!string.IsNullOrWhiteSpace(s)));
        return KindLabel(kind)+(title.Length>0?" · "+title:"");
    }
    internal static string DisplayField(AppDbContext db,string kind,string field,JsonNode? value)
    {
        if(value is null)return "—";
        var specification=Schema[kind]?["fields"]?[field];
        if(specification?["reference"] is {} target)
        {
            var referencedKind=target.GetValue<string>();var type=db.Model.GetEntityTypes().Single(t=>t.ClrType.Name==referencedKind);
            var key=Query(db,"SELECT LocalKey FROM CloudRowIds WHERE Kind=@kind AND GlobalId=@id",("@kind",referencedKind),("@id",value.ToString()));
            var names=new[]{"PersonnelNumber","FirstName","LastName","OrderNumber","ArticleNumber","Name","Subject"}.Where(n=>type.FindProperty(n) is not null).ToArray();
            if(key.Count>0&&names.Length>0)
            {
                var row=Query(db,$"SELECT {string.Join(',',names.Select(n=>$"\"{n}\""))} FROM \"{type.GetTableName()}\" WHERE Id=@id",("@id",key[0][0])).FirstOrDefault();
                if(row is not null)return string.Join(" ",row.Where(v=>v is not DBNull).Select(v=>v.ToString()));
            }
            return KindLabel(referencedKind)+" (nicht mehr vorhanden)";
        }
        if(specification?["type"]?.ToString()=="boolean")return value.ToString().Equals("true",StringComparison.OrdinalIgnoreCase)?"Ja":"Nein";
        if(specification?["type"]?.ToString()=="date"&&DateTime.TryParse(value.ToString(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var date))return date.ToString(field.EndsWith("Utc",StringComparison.Ordinal)?"dd.MM.yyyy HH:mm 'UTC'":"dd.MM.yyyy");
        return value.ToString();
    }
    internal static object? Sql(AppDbContext db,string sql, params (string Key,object? Value)[] args)
    {
        db.Database.OpenConnection(); using var cmd=db.Database.GetDbConnection().CreateCommand(); cmd.CommandText=sql;
        cmd.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
        foreach(var (key,value) in args) { var p=cmd.CreateParameter(); p.ParameterName=key; p.Value=value??DBNull.Value; cmd.Parameters.Add(p); }
        return cmd.ExecuteScalar();
    }
    internal static List<object[]> Query(AppDbContext db,string sql,params (string Key,object? Value)[] args)
    {
        db.Database.OpenConnection(); using var cmd=db.Database.GetDbConnection().CreateCommand(); cmd.CommandText=sql;
        cmd.Transaction=db.Database.CurrentTransaction?.GetDbTransaction();
        foreach(var (key,value) in args) { var p=cmd.CreateParameter();p.ParameterName=key;p.Value=value??DBNull.Value;cmd.Parameters.Add(p); }
        using var reader=cmd.ExecuteReader(); var rows=new List<object[]>(); while(reader.Read()) { var row=new object[reader.FieldCount]; reader.GetValues(row);rows.Add(row); } return rows;
    }
    public static void EnsureTables(AppDbContext db)
    {
        Sql(db,"CREATE TABLE IF NOT EXISTS CloudRowIds (Kind TEXT NOT NULL, LocalKey TEXT NOT NULL, GlobalId TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(Kind,LocalKey), UNIQUE(Kind,GlobalId)); CREATE TABLE IF NOT EXISTS CloudOutbox (Id INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT NOT NULL UNIQUE, UserId INTEGER NOT NULL, Payload TEXT NOT NULL, Attempts INTEGER NOT NULL DEFAULT 0, LastError TEXT NOT NULL DEFAULT '', NextRetryAtUtc TEXT NOT NULL DEFAULT ''); CREATE TABLE IF NOT EXISTS CloudMetadata (Name TEXT PRIMARY KEY, Value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS CloudMessageIds (LocalId INTEGER PRIMARY KEY, GlobalId TEXT NOT NULL UNIQUE); CREATE TABLE IF NOT EXISTS CloudMessageOutbox (OperationId TEXT PRIMARY KEY, UserId INTEGER NOT NULL, Action TEXT NOT NULL, Payload TEXT NOT NULL, LocalId INTEGER NOT NULL, LastError TEXT NOT NULL DEFAULT '');");
    }
    internal static string LocalKey(EntityEntry entry) => string.Join('/',entry.Metadata.FindPrimaryKey()!.Properties.Select(p=>Convert.ToString(entry.Property(p.Name).CurrentValue,System.Globalization.CultureInfo.InvariantCulture)));
    private static CloudLocalIdentity Identity(AppDbContext db,string kind,string localKey)
    {
        var rows=Query(db,"SELECT GlobalId,Version FROM CloudRowIds WHERE Kind=@kind AND LocalKey=@key",("@kind",kind),("@key",localKey));
        if(rows.Count>0)return new(kind,localKey,(string)rows[0][0],Convert.ToInt64(rows[0][1]));
        var id=Guid.NewGuid().ToString("N");
        if(kind=="EmployeeQualification")
        {
            var keys=localKey.Split('/');
            var a=Identity(db,"Employee",keys[0]).GlobalId;var b=Identity(db,"Qualification",keys[1]).GlobalId;
            id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(kind+":"+a+":"+b))).ToLowerInvariant()[..32];
        }
        Sql(db,"INSERT INTO CloudRowIds(Kind,LocalKey,GlobalId,Version) VALUES(@kind,@key,@id,0)",("@kind",kind),("@key",localKey),("@id",id));return new(kind,localKey,id,0);
    }
    private static string Reference(AppDbContext db,string kind,object value) => Identity(db,kind,Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)!).GlobalId;
    internal static FirebaseCloudRecord Serialize(AppDbContext db,EntityEntry entry,bool deleted=false)
    {
        var kind=entry.Metadata.ClrType.Name;var identity=Identity(db,kind,LocalKey(entry));
        var record=new FirebaseCloudRecord { Kind=kind,Id=identity.GlobalId,BaseVersion=CloudEditBaseline.ExpectedVersion(kind,identity.LocalKey,identity.Version),Deleted=deleted };
        foreach(var field in Schema[kind]!["fields"]!.AsObject())
        {
            var value=entry.Property(field.Key).CurrentValue;
            if(value is not null && field.Value!["reference"] is {} target) record.Data[field.Key]=Reference(db,target.GetValue<string>(),value);
            else record.Data[field.Key]=JsonSerializer.SerializeToNode(value,entry.Property(field.Key).Metadata.ClrType);
        }
        TransformHistorical(db,record.Data,kind,true);
        return record;
    }
    private static void TransformHistorical(AppDbContext db,JsonObject data,string kind,bool outgoing)
    {
        if(kind is not ("RecycleBinItem" or "AuditLog"))return;
        var target=data["EntityType"]?.GetValue<string>();var value=data["EntityId"]?.GetValue<string>();
        if(target is null || !Schema.ContainsKey(target))return;
        if(value is not null)
        {
            var composite=db.Model.GetEntityTypes().Single(t=>t.ClrType.Name==target).FindPrimaryKey()!.Properties.Count>1;
            data["EntityId"]=outgoing ? Reference(db,target,value) : composite ? value : EnsureLocal(db,target,value,new JsonObject()).LocalKey;
        }
        if(data["SnapshotJson"]?.GetValue<string>() is { Length:>0 } snapshot)
        {
            var obj=JsonNode.Parse(snapshot)!.AsObject();
            foreach(var field in obj.ToArray())
            {
                var targetKind=field.Key=="Id"?target:Schema[target]!["fields"]?[field.Key]?["reference"]?.GetValue<string>();
                if(targetKind is null || field.Value is null)continue;
                obj[field.Key]=outgoing?Reference(db,targetKind,field.Value.ToString()):JsonValue.Create(long.Parse(EnsureLocal(db,targetKind,field.Value.GetValue<string>(),new()).LocalKey,System.Globalization.CultureInfo.InvariantCulture));
            }
            data["SnapshotJson"]=obj.ToJsonString();
        }
    }
    private static IEnumerable<object> ReadEntities<T>(AppDbContext db) where T:class => db.Set<T>().IgnoreQueryFilters().AsNoTracking().AsEnumerable().Cast<object>();
    public static List<FirebaseCloudRecord> Export(AppDbContext db)
    {
        EnsureTables(db);var entries=new List<EntityEntry>();
        foreach(var type in db.Model.GetEntityTypes().Where(t=>Schema.ContainsKey(t.ClrType.Name)))
            foreach(var entity in (IEnumerable<object>)typeof(FirebaseCloudStorage).GetMethod(nameof(ReadEntities),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(type.ClrType).Invoke(null,new object[]{db})!) entries.Add(db.Entry(entity));
        foreach(var e in entries) Identity(db,e.Metadata.ClrType.Name,LocalKey(e));
        return entries.Select(e=>Serialize(db,e)).ToList();
    }
    internal static List<(EntityEntry Entry,bool Deleted)> Capture(AppDbContext db) => db.ChangeTracker.Entries().Where(e=>Schema.ContainsKey(e.Metadata.ClrType.Name) && e.Metadata.ClrType.Name is not ("AuditLog" or "UserMessage") && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).Select(e=>(e,e.State==EntityState.Deleted)).ToList();
    internal static void Queue(AppDbContext db,List<(EntityEntry Entry,bool Deleted)> changes)
    {
        if(changes.Count==0)return;
        EnsureTables(db);
        foreach(var change in changes) Identity(db,change.Entry.Metadata.ClrType.Name,LocalKey(change.Entry));
        var records=changes.Select(c=>Serialize(db,c.Entry,c.Deleted)).ToList();
        foreach(var group in CloudPacketizer.Pack(records,50)) Sql(db,"INSERT INTO CloudOutbox(OperationId,UserId,Payload) VALUES(@id,@user,@payload)",("@id",Guid.NewGuid().ToString("N")),("@user",SessionService.CurrentUser!.Id),("@payload",JsonSerializer.Serialize(group,FirebaseCompanyClient.Json)));
    }
    internal static CloudLocalIdentity EnsureLocal(AppDbContext db,string kind,string id,JsonObject data)
    {
        var existing=Query(db,"SELECT LocalKey,Version FROM CloudRowIds WHERE Kind=@kind AND GlobalId=@id",("@kind",kind),("@id",id));
        if(existing.Count>0)return new(kind,(string)existing[0][0],id,Convert.ToInt64(existing[0][1]));
        var type=db.Model.GetEntityTypes().Single(t=>t.ClrType.Name==kind); var keys=type.FindPrimaryKey()!.Properties;
        string localKey;
        if(keys.Count==1)
        {
            var max=Convert.ToInt64(Sql(db,$"SELECT COALESCE(MAX(\"{keys[0].Name}\"),0) FROM \"{type.GetTableName()}\""));
            var reserved=Convert.ToInt64(Sql(db,"SELECT COALESCE(MAX(CAST(LocalKey AS INTEGER)),0) FROM CloudRowIds WHERE Kind=@kind",("@kind",kind)));
            localKey=(Math.Max(max,reserved)+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else localKey=string.Join('/',keys.Select(k=>EnsureLocal(db,Schema[kind]!["fields"]![k.Name]!["reference"]!.GetValue<string>(),data[k.Name]!.GetValue<string>(),new()).LocalKey));
        Sql(db,"INSERT INTO CloudRowIds(Kind,LocalKey,GlobalId,Version) VALUES(@kind,@key,@id,0)",("@kind",kind),("@key",localKey),("@id",id));return new(kind,localKey,id,0);
    }
    public static void Apply(AppDbContext db,IReadOnlyList<FirebaseCloudRecord> records,bool skipPending=true)
    {
        EnsureTables(db);
        var pending=new HashSet<string>();
        if(skipPending) foreach(var row in Query(db,"SELECT Payload FROM CloudOutbox")) foreach(var r in JsonSerializer.Deserialize<List<FirebaseCloudRecord>>((string)row[0],FirebaseCompanyClient.Json)!) pending.Add(r.Kind+"_"+r.Id);
        foreach(var r in records) if(Schema.ContainsKey(r.Kind))EnsureLocal(db,r.Kind,r.Id,r.Data);
        foreach(var r in records)
        {
            if(!Schema.ContainsKey(r.Kind)||pending.Contains(r.Kind+"_"+r.Id))continue;
            var identity=EnsureLocal(db,r.Kind,r.Id,r.Data);if(r.Version<=identity.Version)continue;
            var type=db.Model.GetEntityTypes().Single(t=>t.ClrType.Name==r.Kind);var keys=type.FindPrimaryKey()!.Properties;
            var keyParts=identity.LocalKey.Split('/');var values=new Dictionary<string,object?>();for(var i=0;i<keys.Count;i++)values[keys[i].Name]=long.Parse(keyParts[i],System.Globalization.CultureInfo.InvariantCulture);
            var where=string.Join(" AND ",keys.Select(k=>$"\"{k.Name}\"=@{k.Name}"));
            if(r.Deleted)Sql(db,$"DELETE FROM \"{type.GetTableName()}\" WHERE {where}",values.Select(k=>("@"+k.Key,k.Value)).ToArray());
            else
            {
                var data=(JsonObject)r.Data.DeepClone();TransformHistorical(db,data,r.Kind,false);
                foreach(var field in Schema[r.Kind]!["fields"]!.AsObject())
                {
                    var node=data[field.Key];object? value;
                    if(node is null)value=null;
                    else if(field.Value!["reference"] is {} target)value=long.Parse(EnsureLocal(db,target.GetValue<string>(),node.GetValue<string>(),new()).LocalKey,System.Globalization.CultureInfo.InvariantCulture);
                    else value=field.Value!["type"]!.GetValue<string>() switch { "number"=>double.Parse(node.ToJsonString(),System.Globalization.CultureInfo.InvariantCulture),"boolean"=>node.GetValue<bool>()?1L:0L,"date"=>DateTime.Parse(node.GetValue<string>(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind),"time"=>TimeSpan.Parse(node.GetValue<string>(),System.Globalization.CultureInfo.InvariantCulture),_=>node.GetValue<string>() };
                    values[field.Key]=value;
                }
                var names=values.Keys.ToArray();var columns=string.Join(',',names.Select(n=>$"\"{n}\""));var parameters=string.Join(',',names.Select(n=>"@"+n));
                var update=string.Join(',',names.Except(keys.Select(k=>k.Name)).Select(n=>$"\"{n}\"=excluded.\"{n}\""));
                Sql(db,$"INSERT INTO \"{type.GetTableName()}\"({columns}) VALUES({parameters}) ON CONFLICT({string.Join(',',keys.Select(k=>$"\"{k.Name}\""))}) DO UPDATE SET {update}",values.Select(k=>("@"+k.Key,k.Value)).ToArray());
            }
            if(r.Kind=="UserMessage")Sql(db,"INSERT OR IGNORE INTO CloudMessageIds(LocalId,GlobalId) VALUES(@local,@global)",("@local",long.Parse(identity.LocalKey,System.Globalization.CultureInfo.InvariantCulture)),("@global",r.Id));
            Sql(db,"UPDATE CloudRowIds SET Version=@v WHERE Kind=@kind AND GlobalId=@id",("@v",r.Version),("@kind",r.Kind),("@id",r.Id));
        }
    }
    internal static string Metadata(AppDbContext db,string name) => Convert.ToString(Sql(db,"SELECT Value FROM CloudMetadata WHERE Name=@name",("@name",name)))??"";
    internal static void Metadata(AppDbContext db,string name,string value)=>Sql(db,"INSERT INTO CloudMetadata(Name,Value) VALUES(@name,@value) ON CONFLICT(Name) DO UPDATE SET Value=excluded.Value",("@name",name),("@value",value));
    internal static List<CloudQueuedOperation> Pending(AppDbContext db)=>Query(db,"SELECT Id,OperationId,UserId,Payload,Attempts,LastError FROM CloudOutbox WHERE Id=(SELECT MIN(Id) FROM CloudOutbox) AND (NextRetryAtUtc='' OR NextRetryAtUtc<=@now) ORDER BY Id LIMIT 1",("@now",DateTime.UtcNow.ToString("O"))).Select(r=>new CloudQueuedOperation(Convert.ToInt64(r[0]),(string)r[1],Convert.ToInt32(r[2]),(string)r[3],Convert.ToInt32(r[4]),(string)r[5])).ToList();
}

using System.Text;
using System.Text.Json;
namespace Produktionsplanung.App.Services;
internal static class CloudPacketizer
{
    internal static IEnumerable<FirebaseCloudRecord[]> Pack(IEnumerable<FirebaseCloudRecord> records,int maxCount)
    {
        var group=new List<FirebaseCloudRecord>();var bytes=0;var generated=0;var months=new HashSet<string>();
        foreach(var r in records)
        {
            var size=Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r,FirebaseCompanyClient.Json));var extra=0;var keys=new List<string>();
            if(!r.Deleted&&r.Kind is "PlanningAssignment" or "Absence")
            {
                var start=DateTime.Parse(r.Data[r.Kind=="Absence"?"StartDate":"Date"]!.ToJsonString().Trim('"')[..10],System.Globalization.CultureInfo.InvariantCulture);
                var end=r.Kind=="Absence"?DateTime.Parse(r.Data["EndDate"]!.ToJsonString().Trim('"')[..10],System.Globalization.CultureInfo.InvariantCulture):start.AddDays(1);
                extra=(int)Math.Max(0,(end-start).TotalDays+1)*48;
                for(var date=new DateTime(start.Year,start.Month,1);date<=end;date=date.AddMonths(1))keys.Add(r.Data["EmployeeId"]!.ToString()+"_"+date.ToString("yyyy-MM"));
            }
            if(group.Count>0&&(group.Count>=maxCount||bytes+size>450000||generated+size+extra>3500000||months.Union(keys).Count()>180))
            {yield return group.ToArray();group.Clear();bytes=0;generated=0;months.Clear();}
            group.Add(r);bytes+=size;generated+=size+extra;months.UnionWith(keys);
        }
        if(group.Count>0)yield return group.ToArray();
    }
}

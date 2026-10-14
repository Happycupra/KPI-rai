using Produktionsplanung.App.Data;
namespace Produktionsplanung.App.Services;
internal sealed class CloudUserList { public string UserSetVersion { get; set; }="";public List<FirebasePublicUser> Users { get; set; }=new(); }
public static class FirebaseCloudAdministration
{
    public static async Task SaveUserAsync(int id,string username,string displayName,string role,bool active,string password,string expectedVersion,bool ownPassword=false,string currentPassword="")
    {
        var result=await FirebaseCompanyClient.CallAsync<CloudUserList>("cloudManageUser",new { sourceUserId=id,username,displayName,role,isActive=active,password,expectedUserSetVersion=expectedVersion,ownPassword,currentPassword });
        var profile=FirebaseCompanyClient.Profile!;profile.UserSetVersion=result.UserSetVersion;
        using var db=new AppDbContext();FirebaseCompanySync.ApplyUsers(db,result.Users,profile);
        if(id==profile.SourceUserId && password.Length>0)
        {
            var newProfile=await FirebaseCompanyClient.AuthenticateAsync(profile.CompanyCode,username,password);
            newProfile.UserSetVersion=result.UserSetVersion;FirebaseCompanySync.ApplyUsers(db,result.Users,newProfile,password);FirebaseCompanyClient.PersistSession();FirebaseCompanySync.Start();
        }
        else if(id==profile.SourceUserId && (role!=profile.Role||!active))
        { profile.LastVerifiedAtUtc=DateTime.MinValue;FirebaseCompanyClient.PersistSession(); }
    }
}

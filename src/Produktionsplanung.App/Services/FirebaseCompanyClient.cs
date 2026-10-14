using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed class FirebaseCompanyProfile
{
    public string CompanyId { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string SiteName { get; set; } = "";
    public string UserSetVersion { get; set; } = "legacy";
    public string CredentialVersion { get; set; } = "legacy";
    public int SourceUserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime LicenseValidUntilUtc { get; set; }
    public string? DataSetId { get; set; }
    public List<FirebasePublicUser> Users { get; set; } = new();
    public string IdToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime TokenExpiresAtUtc { get; set; }
    public DateTime LastVerifiedAtUtc { get; set; }
}
public sealed record FirebasePublicUser(int Id, string Username, string DisplayName, string Role, bool IsActive);
internal sealed class FirebaseAuthTokens
{
    public string IdToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string ExpiresIn { get; set; } = "3600";
}
public sealed class FirebaseCloudException(HttpStatusCode status, string message, string? details = null) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? Details { get; } = details;
}

public static class FirebaseCompanyClient
{
    internal static HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim TokenGate = new(1,1);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SolutionCompakt.Firebase.Company.v1");
    public static FirebaseCompanyProfile? Profile { get; internal set; }
    public static bool Enabled => AppSettingsService.Load().FirebaseCompanyEnabled;
    private static string SessionPath => Path.Combine(AppPaths.ConfigDirectory, "firebase-company-session.bin");
    public static string CachePath(FirebaseCompanyProfile p) => Path.Combine(AppPaths.DataDirectory,"Cloud",p.CompanyId, $"{p.SourceUserId}-{p.Role}", "produktionsplanung.db");
    public static bool OfflineAccessAllowed => Profile is { } p && DateTime.UtcNow < p.LicenseValidUntilUtc && DateTime.UtcNow < p.LastVerifiedAtUtc.AddHours(24);
    public static void RestoreSession()
    {
        Profile = null;
        if (!Enabled || !File.Exists(SessionPath)) return;
        try
        {
            Profile = JsonSerializer.Deserialize<FirebaseCompanyProfile>(ProtectedData.Unprotect(File.ReadAllBytes(SessionPath),Entropy,DataProtectionScope.CurrentUser),Json);
            if (Profile is null || !Guid.TryParse(Profile.CompanyId, out _) || !UserRoles.All.Contains(Profile.Role)) throw new InvalidOperationException();
            var path = CachePath(Profile);
            if (File.Exists(path)) AppPaths.CloudDatabaseOverride = path;
        }
        catch { Profile = null; }
    }
    public static void PersistSession()
    {
        if (Profile is null) return;
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        var path = SessionPath + ".tmp";
        File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(Profile,Json),Entropy,DataProtectionScope.CurrentUser));
        File.Move(path,SessionPath,true);
    }
    public static async Task<FirebaseCompanyProfile> AuthenticateAsync(string companyCode, string username, string password, CancellationToken token = default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));token=timeout.Token;
        await FirebaseCompanySync.PauseAsync().ConfigureAwait(false);
        var settings = AppSettingsService.Load();
        using var login = await Http.PostAsJsonAsync(RequireHttps(settings.FirebaseAuthEndpoint), new { companyCode, username, password },Json,token).ConfigureAwait(false);
        var body = await ReadAsync<JsonElement>(login,token).ConfigureAwait(false);
        var custom = body.GetProperty("customToken").GetString();
        using var exchange = await Http.PostAsJsonAsync("https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key="+Uri.EscapeDataString(settings.FirebaseWebApiKey),new { token=custom, returnSecureToken=true },Json,token).ConfigureAwait(false);
        var auth = await ReadAsync<FirebaseAuthTokens>(exchange,token).ConfigureAwait(false);
        var previous = Profile;
        Profile = new() { IdToken=auth.IdToken, RefreshToken=auth.RefreshToken, TokenExpiresAtUtc=DateTime.UtcNow.AddSeconds(double.Parse(auth.ExpiresIn,System.Globalization.CultureInfo.InvariantCulture)) };
        try
        {
            var identity = LicenseService.EnsureLocalLicenseIdentity();
            var joined = await CallAsync<FirebaseCompanyProfile>("cloudCompanyJoin",new { installationId=identity.LicenseInstallationId, secret=identity.LicenseSecret },token).ConfigureAwait(false);
            if (!Guid.TryParse(joined.CompanyId,out _) || !UserRoles.All.Contains(joined.Role) || joined.SourceUserId < 1) throw new InvalidOperationException("Ungültige Firmenanmeldung.");
            joined.IdToken=Profile.IdToken; joined.RefreshToken=Profile.RefreshToken; joined.TokenExpiresAtUtc=Profile.TokenExpiresAtUtc; joined.LastVerifiedAtUtc=DateTime.UtcNow;
            Profile=joined; return joined;
        }
        catch { Profile=previous; throw; }
    }
    public static async Task<bool> ValidateCachedAsync(CancellationToken token=default)
    {
        if (Profile is null) return false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));token=timeout.Token;
        try
        {
            var settings=AppSettingsService.Load();
            var validated=await CallAsync<FirebaseCompanyProfile>("cloudCompanyJoin",new { installationId=settings.LicenseInstallationId, secret=settings.LicenseSecret },token).ConfigureAwait(false);
            if(validated.CompanyId!=Profile.CompanyId||validated.SourceUserId!=Profile.SourceUserId||validated.Role!=Profile.Role)return false;
            Profile.LastVerifiedAtUtc=DateTime.UtcNow;Profile.CompanyName=validated.CompanyName;Profile.Username=validated.Username;Profile.DisplayName=validated.DisplayName;Profile.SiteName=validated.SiteName;Profile.LicenseValidUntilUtc=validated.LicenseValidUntilUtc;PersistSession();return true;
        }
        catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException) { return OfflineAccessAllowed; }
        catch(FirebaseCloudException) { Profile.LastVerifiedAtUtc=DateTime.MinValue;PersistSession();return false; }
    }
    public static async Task<T> CallAsync<T>(string endpoint, object payload, CancellationToken token = default)
    {
        if (Profile is null) throw new InvalidOperationException("Bitte mit dem Firmenkonto anmelden.");
        await TokenGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var tokenProfile=Profile;
            if (tokenProfile.TokenExpiresAtUtc <= DateTime.UtcNow.AddMinutes(2))
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await Http.PostAsync("https://securetoken.googleapis.com/v1/token?key="+Uri.EscapeDataString(AppSettingsService.Load().FirebaseWebApiKey), new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"]="refresh_token", ["refresh_token"]=tokenProfile.RefreshToken }),timeout.Token).ConfigureAwait(false);
                if(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    if(ReferenceEquals(Profile,tokenProfile)){tokenProfile.LastVerifiedAtUtc=DateTime.MinValue;PersistSession();}
                    throw new FirebaseCloudException(HttpStatusCode.Unauthorized,"Die Anmeldung ist abgelaufen. Bitte erneut mit dem Firmenkonto anmelden.");
                }
                var refreshed = await ReadAsync<JsonElement>(response,token).ConfigureAwait(false);
                tokenProfile.IdToken=refreshed.GetProperty("id_token").GetString()!; tokenProfile.RefreshToken=refreshed.GetProperty("refresh_token").GetString()!;
                tokenProfile.TokenExpiresAtUtc=DateTime.UtcNow.AddSeconds(double.Parse(refreshed.GetProperty("expires_in").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        finally { TokenGate.Release(); }
        var requestSession=Profile;
        var settings=AppSettingsService.Load();
        using var request = new HttpRequestMessage(HttpMethod.Post,RequireHttps(settings.FirebaseCloudBaseUrl.TrimEnd('/')+"/"+endpoint));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",requestSession.IdToken);
        request.Headers.Add("X-SolutionCompakt-Device",settings.LicenseInstallationId); request.Headers.Add("X-SolutionCompakt-Secret",settings.LicenseSecret);
        request.Content=JsonContent.Create(payload, options:Json);
        using var result=await Http.SendAsync(request,token).ConfigureAwait(false);
        if(result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && ReferenceEquals(Profile,requestSession) && Guid.TryParse(Profile.CompanyId,out _))
        { Profile.LastVerifiedAtUtc=DateTime.MinValue;PersistSession(); }
        return await ReadAsync<T>(result,token).ConfigureAwait(false);
    }
    private static Uri RequireHttps(string uri) => Uri.TryCreate(uri,UriKind.Absolute,out var value) && value.Scheme == "https" ? value : throw new InvalidOperationException("Firebase-Verbindung muss HTTPS verwenden.");
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response,CancellationToken token)
    {
        var json=await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message="Firmenkonto konnte nicht erreicht werden."; string? details=null;
            try { var value=JsonDocument.Parse(json).RootElement; if(value.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String) message=error.GetString()!; if(value.TryGetProperty("details",out var d)) details=d.GetRawText(); } catch { }
            throw new FirebaseCloudException(response.StatusCode,message,details);
        }
        return JsonSerializer.Deserialize<T>(json,Json) ?? throw new InvalidOperationException("Ungültige Firebase-Antwort.");
    }
}

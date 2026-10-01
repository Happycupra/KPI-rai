using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Produktionsplanung.App.Services;

public sealed class ProviderLicenseRecord
{
    public string InstallationId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ValidUntilUtc { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public string LastAppVersion { get; set; } = string.Empty;
    public bool HasRecoveryCode { get; set; }
    public DateTime? RecoveryCodeUpdatedAtUtc { get; set; }

    public string EffectiveStatus =>
        string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) && ValidUntilUtc is { } until && until <= DateTime.UtcNow
            ? "expired"
            : Status;
    public string ValidUntilText => ValidUntilUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string LastCheckedText => LastCheckedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}

public static class ProviderAdminService
{
    public const string OwnerEmail = LicenseService.AdministratorEmail;

    private const string SignInUrl = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=";
    private const string RefreshUrl = "https://securetoken.googleapis.com/v1/token?key=";
    private const string AdminLicensesEndpoint = "https://europe-west1-solution-compact.cloudfunctions.net/adminLicenses";
    private const string AdminRecoveryEndpoint = "https://europe-west1-solution-compact.cloudfunctions.net/adminGetRecoveryCode";
    private const string AdminExtendEndpoint = "https://europe-west1-solution-compact.cloudfunctions.net/adminExtendLicense";
    private const string AdminStatusEndpoint = "https://europe-west1-solution-compact.cloudfunctions.net/adminSetLicenseStatus";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static ProviderSession? session;

    public static bool IsSignedIn => session is not null;

    public static async Task SignInAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Bitte das Anbieter-Passwort eingeben.");

        var settings = AppSettingsService.Load();
        var url = SignInUrl + Uri.EscapeDataString(settings.FirebaseWebApiKey);
        using var response = await Http.PostAsJsonAsync(url, new
        {
            email = OwnerEmail,
            password,
            returnSecureToken = true
        }, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(HumanFirebaseError(json));

        var auth = JsonSerializer.Deserialize<FirebaseSignInResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Die Anbieter-Anmeldung lieferte keine gültige Antwort.");
        if (!string.Equals(auth.Email, OwnerEmail, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(auth.IdToken))
            throw new InvalidOperationException("Dieses Konto ist nicht für die Anbieter-Verwaltung freigeschaltet.");

        session = new ProviderSession(
            auth.IdToken,
            auth.RefreshToken,
            DateTime.UtcNow.AddSeconds(ParseExpiresIn(auth.ExpiresIn)));
    }

    public static void SignOut() => session = null;

    public static async Task<IReadOnlyList<ProviderLicenseRecord>> LoadLicensesAsync(CancellationToken cancellationToken = default)
    {
        var result = await AdminCallAsync<LicenseListResponse>(AdminLicensesEndpoint, new { }, cancellationToken);
        return (result.Licenses ?? new List<ProviderLicenseRecord>())
            .OrderByDescending(x => x.RequestedAtUtc)
            .ThenBy(x => x.CompanyName)
            .ToList();
    }

    public static async Task ExtendLicenseAsync(string installationId, int days, CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 3650)
            throw new InvalidOperationException("Bitte 1 bis 3650 Tage eingeben.");
        await AdminCallAsync<BasicResponse>(AdminExtendEndpoint, new { installationId, days }, cancellationToken);
    }

    public static async Task SuspendLicenseAsync(string installationId, CancellationToken cancellationToken = default) =>
        await AdminCallAsync<BasicResponse>(AdminStatusEndpoint, new { installationId, status = "suspended" }, cancellationToken);

    public static async Task<string> GetRecoveryCodeAsync(string installationId, CancellationToken cancellationToken = default)
    {
        var result = await AdminCallAsync<RecoveryResponse>(AdminRecoveryEndpoint, new { installationId }, cancellationToken);
        return result.RecoveryCode ?? string.Empty;
    }

    private static async Task<T> AdminCallAsync<T>(string endpoint, object body, CancellationToken cancellationToken)
    {
        var token = await GetValidIdTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(HumanApiError(json, response.StatusCode));

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("Die Anbieter-Verwaltung lieferte keine gültige Antwort.");
    }

    private static async Task<string> GetValidIdTokenAsync(CancellationToken cancellationToken)
    {
        var current = session ?? throw new InvalidOperationException("Bitte zuerst als Anbieter anmelden.");
        if (current.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2))
            return current.IdToken;

        if (string.IsNullOrWhiteSpace(current.RefreshToken))
            throw new InvalidOperationException("Die Anbieter-Sitzung ist abgelaufen. Bitte erneut anmelden.");

        var settings = AppSettingsService.Load();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken
        });
        using var response = await Http.PostAsync(RefreshUrl + Uri.EscapeDataString(settings.FirebaseWebApiKey), content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            session = null;
            throw new InvalidOperationException("Die Anbieter-Sitzung ist abgelaufen. Bitte erneut anmelden.");
        }

        var refreshed = JsonSerializer.Deserialize<FirebaseRefreshResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Die Anbieter-Sitzung konnte nicht erneuert werden.");
        session = new ProviderSession(
            refreshed.IdToken,
            string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? current.RefreshToken : refreshed.RefreshToken,
            DateTime.UtcNow.AddSeconds(ParseExpiresIn(refreshed.ExpiresIn)));
        return session.IdToken;
    }

    private static int ParseExpiresIn(string? value) =>
        int.TryParse(value, out var seconds) && seconds > 0 ? seconds : 3600;

    private static string HumanFirebaseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;
            return code switch
            {
                "INVALID_LOGIN_CREDENTIALS" or "INVALID_PASSWORD" or "EMAIL_NOT_FOUND" => "E-Mail oder Passwort ist nicht korrekt.",
                "USER_DISABLED" => "Das Anbieter-Konto ist deaktiviert.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Zu viele Anmeldeversuche. Bitte später erneut versuchen.",
                _ => "Anbieter-Anmeldung fehlgeschlagen: " + code
            };
        }
        catch
        {
            return "Anbieter-Anmeldung fehlgeschlagen.";
        }
    }

    private static string HumanApiError(string json, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? $"Online-Anfrage fehlgeschlagen ({(int)statusCode}).";
            if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? $"Online-Anfrage fehlgeschlagen ({(int)statusCode}).";
        }
        catch { }
        return $"Online-Anfrage fehlgeschlagen ({(int)statusCode}).";
    }

    private sealed record ProviderSession(string IdToken, string RefreshToken, DateTime ExpiresAtUtc);

    private sealed class FirebaseSignInResponse
    {
        public string IdToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string ExpiresIn { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class FirebaseRefreshResponse
    {
        [JsonPropertyName("id_token")] public string IdToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public string ExpiresIn { get; set; } = string.Empty;
    }

    private sealed class LicenseListResponse
    {
        public List<ProviderLicenseRecord>? Licenses { get; set; }
    }

    private sealed class RecoveryResponse
    {
        public string? RecoveryCode { get; set; }
    }

    private sealed class BasicResponse
    {
        public bool Ok { get; set; }
    }
}

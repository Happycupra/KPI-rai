using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed record OnlineWeekPlanPublication(string WeekId, DateTime PublishedAtUtc, string PublishedBy);

public static class OnlineWeekPlanPublisher
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(150) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<OnlineWeekPlanPublication> PublishAsync(OnlineWeekPlanSnapshot snapshot, string password)
    {
        if (!SessionService.IsAdministrator || SessionService.CurrentUser is not { } user)
            throw new InvalidOperationException("Nur Administratoren dürfen den Wochenplan veröffentlichen.");
        return await PublishAndRememberAsync(snapshot, user.Username, password, AppSettingsService.Load(), Http);
    }

    internal static async Task<OnlineWeekPlanPublication> PublishAndRememberAsync(
        OnlineWeekPlanSnapshot snapshot, string username, string password, AppSettings settings, HttpClient http)
    {
        if (!OnlineWeekPlanService.IsFirebaseConfigured(settings))
            throw new InvalidOperationException("Bitte den Online-Wochenplan in den Online-Einstellungen aktivieren und vollständig konfigurieren.");
        if (snapshot.CompanyId != settings.CompanyId ||
            !string.Equals(snapshot.CompanyCode, settings.CompanyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der Wochenplan gehört nicht zur aktuellen Firma.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Bitte dein App-Passwort eingeben.");

        using var login = await SendAsync(http, settings.FirebaseAuthEndpoint,
            new { companyCode = settings.CompanyCode, username, password }, null, "Online-Anmeldung").ConfigureAwait(false);
        var customToken = RequiredString(login.RootElement, "customToken");
        using var session = await SendAsync(http,
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key=" + Uri.EscapeDataString(settings.FirebaseWebApiKey),
            new { token = customToken, returnSecureToken = true }, null, "Online-Anmeldung").ConfigureAwait(false);
        var idToken = RequiredString(session.RootElement, "idToken");
        using var publication = await SendAsync(http, settings.FirebasePublishEndpoint, snapshot, idToken,
            "Veröffentlichung").ConfigureAwait(false);
        var root = publication.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            RequiredString(root, "weekId") != snapshot.WeekId ||
            !DateTimeOffset.TryParse(RequiredString(root, "publishedAtUtc"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp))
            throw new InvalidOperationException("Keine gültige Veröffentlichungsbestätigung erhalten. Bitte den Online-Plan prüfen.");

        var receipt = new OnlineWeekPlanPublication(snapshot.WeekId, timestamp.UtcDateTime, username);
        try
        {
            AppSettingsService.Update(value =>
            {
                value.OnlineWeekPublications ??= new();
                value.OnlineWeekPublications[ReceiptKey(snapshot.CompanyId, snapshot.WeekId)] = receipt;
            });
        }
        catch
        {
            // The upload has succeeded. Do not report it as failed because the local receipt could not be written.
            throw new PublicationReceiptException(receipt);
        }
        return receipt;
    }

    public static string ReceiptKey(string companyId, string weekId) => companyId + "/" + weekId;

    private static string RequiredString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        throw new InvalidOperationException("Unvollständige Serverantwort. Bitte den Online-Plan prüfen.");
    }

    private static async Task<JsonDocument> SendAsync(HttpClient http, string endpoint, object body, string? token, string operation)
    {
        if (!OnlineWeekPlanService.IsHttpsUrl(endpoint))
            throw new InvalidOperationException("Für die Online-Veröffentlichung ist eine HTTPS-Adresse erforderlich.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        { Content = JsonContent.Create(body, options: JsonOptions) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Never surface raw token-service responses or credentials in the UI/logs.
            var detail = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Anmeldung oder Lizenz ungültig. Bitte App-Passwort und Online-Zugang prüfen.",
                System.Net.HttpStatusCode.Forbidden => "Keine Berechtigung. Bitte Administrator und Firmenzuordnung prüfen.",
                _ => $"Server meldet HTTP {(int)response.StatusCode}. Bitte später erneut versuchen."
            };
            throw new InvalidOperationException(operation + ": " + detail);
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}

public sealed class PublicationReceiptException : Exception
{
    public OnlineWeekPlanPublication Publication { get; }
    public PublicationReceiptException(OnlineWeekPlanPublication publication)
        : base("Der Plan ist online veröffentlicht, aber die Zeitangabe konnte auf diesem Gerät nicht gespeichert werden.")
    { Publication = publication; }
}

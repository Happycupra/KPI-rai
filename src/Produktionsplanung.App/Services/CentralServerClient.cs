using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

internal sealed class CentralDesktopAuthRequest
{
    public string CompanyId { get; init; } = string.Empty;
    public string CompanyCode { get; init; } = string.Empty;
    public string InstallationId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public int SourceUserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
}

internal sealed class CentralDesktopAuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
}

public static class CentralServerClient
{
    private static readonly SemaphoreSlim AuthGate = new(1, 1);
    private static string accessToken = string.Empty;
    private static DateTime accessTokenExpiresAtUtc = DateTime.MinValue;
    private static int accessTokenUserId;
    private static string accessTokenCompanyId = string.Empty;
    private static Uri? serverBaseUri;

    public static async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!CentralModeService.IsEnabled)
            throw new InvalidOperationException("Der zentrale Mehrbenutzerbetrieb ist nicht aktiviert.");
        if (!SessionService.IsAuthenticated || SessionService.CurrentUser is null)
            throw new InvalidOperationException("Für den zentralen Serverzugriff ist eine Anmeldung erforderlich.");

        var appSettings = AppSettingsService.Load();
        var user = SessionService.CurrentUser;
        if (TokenIsUsable(appSettings.CompanyId, user.Id))
            return accessToken;

        await AuthGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            appSettings = AppSettingsService.Load();
            user = SessionService.CurrentUser
                ?? throw new InvalidOperationException("Die Benutzersitzung ist nicht mehr aktiv.");
            if (TokenIsUsable(appSettings.CompanyId, user.Id))
                return accessToken;

            var central = CentralModeService.RequireEnabledSettings();
            if (!Uri.TryCreate(central.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out serverBaseUri))
                throw new InvalidOperationException("Zentrale Serveradresse ist ungültig.");

            var request = new CentralDesktopAuthRequest
            {
                CompanyId = appSettings.CompanyId,
                CompanyCode = appSettings.CompanyCode,
                InstallationId = appSettings.LicenseInstallationId,
                Secret = appSettings.LicenseSecret,
                SourceUserId = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"
            };

            using var http = CreateHttpClient();
            using var response = await http.PostAsJsonAsync("api/v1/auth/desktop", request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await CreateServerExceptionAsync(response, "Server-Anmeldung fehlgeschlagen", cancellationToken)
                    .ConfigureAwait(false);

            var auth = await response.Content.ReadFromJsonAsync<CentralDesktopAuthResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
                throw new InvalidOperationException("Server hat kein gültiges Zugriffstoken geliefert.");

            accessToken = auth.AccessToken;
            accessTokenExpiresAtUtc = auth.ExpiresAtUtc;
            accessTokenUserId = user.Id;
            accessTokenCompanyId = appSettings.CompanyId;
            return accessToken;
        }
        finally
        {
            AuthGate.Release();
        }
    }

    public static Uri GetServerBaseUri()
    {
        if (serverBaseUri is not null)
            return serverBaseUri;
        var central = CentralModeService.RequireEnabledSettings();
        if (!Uri.TryCreate(central.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out serverBaseUri))
            throw new InvalidOperationException("Zentrale Serveradresse ist ungültig.");
        return serverBaseUri;
    }

    public static async Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, relativePath, null, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, relativePath, request, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TResponse> PostAsync<TResponse>(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, relativePath, null, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public static void InvalidateAuthentication()
    {
        accessToken = string.Empty;
        accessTokenExpiresAtUtc = DateTime.MinValue;
        accessTokenUserId = 0;
        accessTokenCompanyId = string.Empty;
    }

    private static bool TokenIsUsable(string companyId, int userId) =>
        !string.IsNullOrWhiteSpace(accessToken) &&
        accessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5) &&
        accessTokenUserId == userId &&
        string.Equals(accessTokenCompanyId, companyId, StringComparison.OrdinalIgnoreCase);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType());

            using var http = CreateHttpClient();
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                InvalidateAuthentication();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var exception = await CreateServerExceptionAsync(response, "Zentraler Serverzugriff fehlgeschlagen", cancellationToken)
                    .ConfigureAwait(false);
                response.Dispose();
                throw exception;
            }
            return response;
        }

        throw new InvalidOperationException("Zentraler Serverzugriff konnte nicht authentifiziert werden.");
    }

    private static HttpClient CreateHttpClient() => new()
    {
        BaseAddress = GetServerBaseUri(),
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static async Task<T> ReadRequiredJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("Der zentrale Server hat eine leere Antwort geliefert.");
    }

    private static async Task<Exception> CreateServerExceptionAsync(
        HttpResponseMessage response,
        string prefix,
        CancellationToken cancellationToken)
    {
        var detail = string.Empty;
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var json = JsonDocument.Parse(text);
                    if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        detail = error.GetString() ?? string.Empty;
                    else
                        detail = text;
                }
                catch
                {
                    detail = text;
                }
            }
        }
        catch
        {
            // Preserve the HTTP status even if the response body cannot be read.
        }

        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim();
        return new InvalidOperationException($"{prefix} ({(int)response.StatusCode}).{suffix}".Trim());
    }
}

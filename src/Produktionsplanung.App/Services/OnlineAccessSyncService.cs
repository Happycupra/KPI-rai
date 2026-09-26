using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record OnlineAccessSyncResult(bool Success, string Message, int UserCount);

internal sealed class OnlineAccessSyncRequest
{
    public string InstallationId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public string CompanyId { get; init; } = string.Empty;
    public string CompanyCode { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public IReadOnlyList<OnlineAccessUser> Users { get; init; } = Array.Empty<OnlineAccessUser>();
}

internal sealed class OnlineAccessUser
{
    public int SourceUserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string UsernameNormalized { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string PasswordHash { get; init; } = string.Empty;
    public string PasswordSalt { get; init; } = string.Empty;
}

public static class OnlineAccessSyncService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public static void QueueSync()
    {
        _ = SyncIgnoringErrorsAsync();
    }

    public static async Task<OnlineAccessSyncResult> TrySyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await SyncLock.WaitAsync(0, cancellationToken))
            return new OnlineAccessSyncResult(true, "Synchronisation läuft bereits.", 0);

        try
        {
            var settings = LicenseService.EnsureLocalLicenseIdentity();
            if (string.IsNullOrWhiteSpace(settings.CompanyId) ||
                string.IsNullOrWhiteSpace(settings.CompanyCode))
                return new OnlineAccessSyncResult(false, "Firma ist lokal noch nicht vollständig eingerichtet.", 0);

            if (!Uri.TryCreate(settings.FirebaseUserSyncEndpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
                return new OnlineAccessSyncResult(false, "Online-Benutzersynchronisation ist nicht gültig konfiguriert.", 0);

            List<UserAccount> users;
            using (var db = new AppDbContext())
            {
                users = db.UserAccounts.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .ToList();
            }

            if (users.Count == 0)
                return new OnlineAccessSyncResult(false, "Es sind keine Benutzer zum Synchronisieren vorhanden.", 0);

            var request = BuildRequest(settings, users);
            using var response = await Http.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
            var message = await ReadMessageAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new OnlineAccessSyncResult(false, message ?? "Online-Zugang konnte nicht synchronisiert werden.", users.Count);

            AppSettingsService.Update(value => value.LastOnlineAccessSyncAtUtc = DateTime.UtcNow);
            return new OnlineAccessSyncResult(
                true,
                message ?? $"{users.Count} Benutzer für den Online-Wochenplan synchronisiert.",
                users.Count);
        }
        catch (Exception ex)
        {
            return new OnlineAccessSyncResult(false, "Online-Zugang konnte nicht synchronisiert werden. " + ex.Message, 0);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    internal static OnlineAccessSyncRequest BuildRequest(AppSettings settings, IReadOnlyList<UserAccount> users)
    {
        var normalizedNames = users
            .Select(x => x.Username.Trim().ToLowerInvariant())
            .ToList();
        if (normalizedNames.Count != normalizedNames.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("Benutzernamen dürfen sich für den Online-Zugang nicht nur durch Gross-/Kleinschreibung unterscheiden.");

        return new OnlineAccessSyncRequest
        {
            InstallationId = settings.LicenseInstallationId,
            Secret = settings.LicenseSecret,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            CompanyId = settings.CompanyId,
            CompanyCode = CompanyIdentityService.NormalizeCode(settings.CompanyCode),
            CompanyName = settings.CompanyName.Trim(),
            Users = users.Select(x => new OnlineAccessUser
            {
                SourceUserId = x.Id,
                Username = x.Username.Trim(),
                UsernameNormalized = x.Username.Trim().ToLowerInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.Username.Trim() : x.DisplayName.Trim(),
                Role = x.Role,
                IsActive = x.IsActive,
                PasswordHash = x.PasswordHash,
                PasswordSalt = x.PasswordSalt
            }).ToList()
        };
    }

    private static async Task SyncIgnoringErrorsAsync()
    {
        try
        {
            await TrySyncAsync();
        }
        catch
        {
            // Online-Zugang darf lokale Arbeit nicht blockieren.
        }
    }

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
        }
        catch
        {
            // Use generic fallback below.
        }

        return response.ReasonPhrase;
    }
}

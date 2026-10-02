using System.Net.Http;
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
    internal static HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    private static CancellationTokenSource? retryCancellation;
    internal static bool BackgroundSyncEnabled { get; set; } = true;

    public static void QueueSync(bool preservePending = false)
    {
        // Persist before scheduling: a crash/offline save must not lose this intent.
        var settings = AppSettingsService.Update(value =>
        {
            if (!preservePending || value.PendingOnlineAccessSync is null)
                value.PendingOnlineAccessSync = new PendingOnlineAccessSync();
        });
        var pending = settings.PendingOnlineAccessSync!;
        if (BackgroundSyncEnabled && pending.NextRetryAtUtc <= DateTime.UtcNow)
            _ = Task.Run(() => SyncIgnoringErrorsAsync(pending.PayloadVersion));
    }

    public static void StartRetryWorker()
    {
        if (!BackgroundSyncEnabled || retryCancellation is not null) return;
        retryCancellation = new CancellationTokenSource();
        var token = retryCancellation.Token;
        _ = Task.Run(() => RunRetryWorkerAsync(token));
    }

    public static void StopRetryWorker()
    {
        retryCancellation?.Cancel();
        retryCancellation?.Dispose();
        retryCancellation = null;
    }

    private static async Task RunRetryWorkerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try
                {
                    var pending = AppSettingsService.Load().PendingOnlineAccessSync;
                    if (pending is not null && pending.NextRetryAtUtc <= DateTime.UtcNow)
                        await TrySyncPendingAsync(pending.PayloadVersion, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // A temporary settings/disk failure must not permanently stop the worker.
                    System.Diagnostics.Trace.TraceError("Online sync retry failed: {0}", ex.Message);
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public static Task<OnlineAccessSyncResult> TrySyncAsync(CancellationToken cancellationToken = default) =>
        TrySyncCoreAsync(null, cancellationToken);

    internal static Task<OnlineAccessSyncResult> TrySyncPendingAsync(string payloadVersion, CancellationToken cancellationToken = default) =>
        TrySyncCoreAsync(payloadVersion, cancellationToken);

    private static async Task<OnlineAccessSyncResult> TrySyncCoreAsync(string? queuedVersion, CancellationToken cancellationToken)
    {
        await SyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? payloadVersion = null;
        OnlineAccessSyncResult? result = null;
        OnlineAccessSyncResult Finish(bool success, string message, int count)
        {
            result = new OnlineAccessSyncResult(success, message, count);
            return result;
        }

        try
        {
            if (queuedVersion is not null)
            {
                var pending = AppSettingsService.Load().PendingOnlineAccessSync;
                // Another waiter may have completed this revision, or scheduled its retry.
                if (pending is null || pending.PayloadVersion != queuedVersion || pending.NextRetryAtUtc > DateTime.UtcNow)
                    return new OnlineAccessSyncResult(true, "Kein fälliger Synchronisationsauftrag.", 0);
            }
            LicenseService.EnsureLocalLicenseIdentity();
            var settings = AppSettingsService.Update(value =>
                value.PendingOnlineAccessSync ??= new PendingOnlineAccessSync());
            payloadVersion = settings.PendingOnlineAccessSync!.PayloadVersion;
            var now = DateTime.UtcNow;
            if (!string.Equals(settings.LicenseStatus, "active", StringComparison.OrdinalIgnoreCase) ||
                settings.LicenseValidUntilUtc is not { } validUntil ||
                validUntil <= now)
                return Finish(false, "Online-Zugang wird nur bei aktiver Lizenz synchronisiert.", 0);

            if (string.IsNullOrWhiteSpace(settings.CompanyId) ||
                string.IsNullOrWhiteSpace(settings.CompanyCode))
                return Finish(false, "Firma ist lokal noch nicht vollständig eingerichtet.", 0);

            if (!Uri.TryCreate(settings.FirebaseUserSyncEndpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
                return Finish(false, "Online-Benutzersynchronisation ist nicht gültig konfiguriert.", 0);

            List<UserAccount> users;
            using (var db = new AppDbContext())
            {
                users = db.UserAccounts.AsNoTracking()
                    .OrderBy(x => x.Id)
                    .ToList();
            }

            if (users.Count == 0)
                return Finish(false, "Es sind keine Benutzer zum Synchronisieren vorhanden.", 0);

            var request = BuildRequest(settings, users);
            using var response = await Http.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken).ConfigureAwait(false);
            var message = await ReadMessageAsync(response, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return Finish(false, message ?? "Online-Zugang konnte nicht synchronisiert werden.", users.Count);

            return Finish(
                true,
                message ?? $"{users.Count} Benutzer für den Online-Wochenplan synchronisiert.",
                users.Count);
        }
        catch (Exception ex)
        {
            return Finish(false, "Online-Zugang konnte nicht synchronisiert werden. " + ex.Message, 0);
        }
        finally
        {
            try
            {
                if (payloadVersion is not null && result is not null)
                    RecordResult(payloadVersion, result, DateTime.UtcNow);
            }
            finally { SyncLock.Release(); }
        }
    }

    internal static void RecordResult(string payloadVersion, OnlineAccessSyncResult result, DateTime now)
    {
        AppSettingsService.Update(settings =>
        {
            if (result.Success) settings.LastOnlineAccessSyncAtUtc = now;
            var pending = settings.PendingOnlineAccessSync;
            // A newer local edit queued while HTTP was in flight must survive this acknowledgement.
            if (pending?.PayloadVersion != payloadVersion) return;
            if (result.Success) settings.PendingOnlineAccessSync = null;
            else
            {
                pending.Attempts = Math.Min(pending.Attempts + 1, 1000);
                pending.LastError = result.Message;
                pending.NextRetryAtUtc = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(pending.Attempts - 1, 7))));
            }
        });
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

    private static async Task SyncIgnoringErrorsAsync(string payloadVersion)
    {
        try
        {
            await TrySyncPendingAsync(payloadVersion).ConfigureAwait(false);
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
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
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

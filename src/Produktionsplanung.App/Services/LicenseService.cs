using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed record LicenseGateResult(
    bool Allowed,
    bool IsTrial,
    int TrialDaysRemaining,
    string Status,
    DateTime? ValidUntilUtc,
    string Message);

public sealed record LicenseActionResult(
    bool Success,
    string Status,
    DateTime? ValidUntilUtc,
    string Message);

public static class LicenseService
{
    public const int TrialDays = 7;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AppSettings EnsureLocalLicenseIdentity()
    {
        return AppSettingsService.Update(settings =>
        {
            settings.TrialStartedAtUtc ??= DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(settings.LicenseInstallationId))
                settings.LicenseInstallationId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(settings.LicenseSecret))
                settings.LicenseSecret = CreateSecret();
        });
    }

    public static LicenseGateResult GetLocalTrialState()
    {
        var settings = EnsureLocalLicenseIdentity();
        _ = TrySyncPendingRecoveryCodeAsync();
        var now = DateTime.UtcNow;
        var trialEnd = settings.TrialStartedAtUtc!.Value.AddDays(TrialDays);
        var remaining = Math.Max(0, (int)Math.Ceiling((trialEnd - now).TotalDays));
        var allowed = now < trialEnd;
        return new LicenseGateResult(
            allowed,
            allowed,
            remaining,
            settings.LicenseStatus,
            settings.LicenseValidUntilUtc,
            allowed
                ? $"Testphase aktiv · noch {remaining} Tag(e)."
                : "Die 7-tägige Testphase ist abgelaufen.");
    }

    public static async Task<LicenseGateResult> EvaluateStartupAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureLocalLicenseIdentity();
        _ = TrySyncPendingRecoveryCodeAsync();
        var now = DateTime.UtcNow;
        var trialEnd = settings.TrialStartedAtUtc!.Value.AddDays(TrialDays);

        if (now < trialEnd)
        {
            var remaining = Math.Max(1, (int)Math.Ceiling((trialEnd - now).TotalDays));
            return new LicenseGateResult(
                true,
                true,
                remaining,
                settings.LicenseStatus,
                settings.LicenseValidUntilUtc,
                $"Testphase aktiv · noch {remaining} Tag(e).");
        }

        var online = await CheckOnlineAsync(cancellationToken);
        if (!online.Success)
        {
            return new LicenseGateResult(
                false,
                false,
                0,
                online.Status,
                online.ValidUntilUtc,
                "Nach Ablauf der 7-tägigen Testphase ist beim Programmstart eine Internetverbindung zur Lizenzprüfung erforderlich. " +
                online.Message);
        }

        var active = string.Equals(online.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                     online.ValidUntilUtc is { } validUntil &&
                     validUntil > now;

        return new LicenseGateResult(
            active,
            false,
            0,
            online.Status,
            online.ValidUntilUtc,
            active
                ? $"Lizenz freigeschaltet bis {online.ValidUntilUtc!.Value.ToLocalTime():d}."
                : online.Message);
    }

    public static async Task<LicenseActionResult> RequestRegistrationAsync(
        string companyName,
        string email,
        CancellationToken cancellationToken = default)
    {
        var settings = EnsureLocalLicenseIdentity();
        companyName = (companyName ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (companyName.Length < 2)
            return new LicenseActionResult(false, settings.LicenseStatus, settings.LicenseValidUntilUtc, "Bitte einen gültigen Firmennamen eingeben.");
        if (!email.Contains('@') || email.Length < 5)
            return new LicenseActionResult(false, settings.LicenseStatus, settings.LicenseValidUntilUtc, "Bitte eine gültige E-Mail-Adresse eingeben.");

        try
        {
            var response = await Http.PostAsJsonAsync(
                settings.LicenseRequestEndpoint,
                new
                {
                    installationId = settings.LicenseInstallationId,
                    secret = settings.LicenseSecret,
                    companyName,
                    email,
                    appVersion = CurrentVersionText()
                },
                JsonOptions,
                cancellationToken);

            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new LicenseActionResult(false, payload.Status, payload.ValidUntilUtc, payload.Message ?? "Registrierung konnte nicht gesendet werden.");

            AppSettingsService.Update(value =>
            {
                value.LicenseEmail = email;
                value.LicenseStatus = payload.Status;
                value.LicenseValidUntilUtc = payload.ValidUntilUtc;
                if (string.IsNullOrWhiteSpace(value.CompanyName) ||
                    string.Equals(value.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase))
                    value.CompanyName = companyName;
            });

            await TrySyncPendingRecoveryCodeAsync(cancellationToken);

            return new LicenseActionResult(
                true,
                payload.Status,
                payload.ValidUntilUtc,
                payload.Message ?? "Registrierungsanfrage wurde gesendet.");
        }
        catch (Exception ex)
        {
            return new LicenseActionResult(false, settings.LicenseStatus, settings.LicenseValidUntilUtc,
                "Keine Verbindung zum Lizenzserver. " + ex.Message);
        }
    }

    public static async Task<LicenseActionResult> CheckOnlineAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureLocalLicenseIdentity();
        try
        {
            var response = await Http.PostAsJsonAsync(
                settings.LicenseStatusEndpoint,
                new
                {
                    installationId = settings.LicenseInstallationId,
                    secret = settings.LicenseSecret,
                    appVersion = CurrentVersionText()
                },
                JsonOptions,
                cancellationToken);

            var payload = await ReadPayloadAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new LicenseActionResult(false, payload.Status, payload.ValidUntilUtc, payload.Message ?? "Lizenzprüfung fehlgeschlagen.");

            AppSettingsService.Update(value =>
            {
                value.LicenseStatus = payload.Status;
                value.LicenseValidUntilUtc = payload.ValidUntilUtc;
                value.LicenseLastCheckedAtUtc = DateTime.UtcNow;
            });

            await TrySyncPendingRecoveryCodeAsync(cancellationToken);

            return new LicenseActionResult(
                true,
                payload.Status,
                payload.ValidUntilUtc,
                payload.Message ?? "Lizenzstatus aktualisiert.");
        }
        catch (Exception ex)
        {
            return new LicenseActionResult(false, settings.LicenseStatus, settings.LicenseValidUntilUtc,
                "Lizenzserver nicht erreichbar. " + ex.Message);
        }
    }

    public static void QueueRecoveryCodeForSupportSync(string recoveryCode)
    {
        var normalized = (recoveryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        AppSettingsService.Update(settings =>
        {
            settings.RecoveryCodePendingSupportSync = normalized;
        });

        _ = TrySyncPendingRecoveryCodeAsync();
    }

    public static async Task<bool> TrySyncPendingRecoveryCodeAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureLocalLicenseIdentity();
        var recoveryCode = settings.RecoveryCodePendingSupportSync?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(recoveryCode))
            return true;

        try
        {
            var response = await Http.PostAsJsonAsync(
                settings.LicenseRecoverySyncEndpoint,
                new
                {
                    installationId = settings.LicenseInstallationId,
                    secret = settings.LicenseSecret,
                    recoveryCode,
                    companyName = settings.CompanyName,
                    appVersion = CurrentVersionText()
                },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            AppSettingsService.Update(value =>
            {
                if (string.Equals(value.RecoveryCodePendingSupportSync, recoveryCode, StringComparison.Ordinal))
                    value.RecoveryCodePendingSupportSync = string.Empty;
                value.RecoveryCodeLastSupportSyncAtUtc = DateTime.UtcNow;
            });
            return true;
        }
        catch
        {
            // Offline use stays possible. The pending code is retried on a later start/check.
            return false;
        }
    }

    public static string CurrentVersionText() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static async Task<LicenseServerPayload> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<LicenseServerPayload>(JsonOptions, cancellationToken);
            return payload ?? new LicenseServerPayload { Status = "unknown", Message = response.ReasonPhrase };
        }
        catch
        {
            return new LicenseServerPayload { Status = "unknown", Message = response.ReasonPhrase };
        }
    }

    private static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class LicenseServerPayload
    {
        public bool Ok { get; set; }
        public string Status { get; set; } = "unknown";
        public DateTime? ValidUntilUtc { get; set; }
        public string? Message { get; set; }
    }
}

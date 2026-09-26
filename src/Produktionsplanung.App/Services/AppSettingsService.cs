using System.IO;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed class AppSettings
{
    public string CompanyName { get; set; } = "SolutionCompakt";
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyRegistrationMode { get; set; } = string.Empty;
    public DateTime? CompanyRegisteredAtUtc { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string DefaultBackupDirectory { get; set; } = AppPaths.IsPortableMode ? "Backups" : AppPaths.BackupsDirectory;
    public string DefaultExportDirectory { get; set; } = AppPaths.IsPortableMode ? "Exports" : AppPaths.ExportsDirectory;
    public bool AutoBackupOnExit { get; set; } = true;
    public int BackupRetentionCount { get; set; } = 10;
    public string CsvDelimiter { get; set; } = ";";
    public bool IncludeUtf8Bom { get; set; } = true;
    public DateTime? LastSuccessfulBackupAtLocal { get; set; }
    public string LastSuccessfulBackupPath { get; set; } = string.Empty;

    // Firebase-ready online week plan. These values are configuration identifiers/endpoints,
    // never service-account credentials or plaintext user passwords.
    public bool OnlineWeekPlanEnabled { get; set; }
    public string FirebaseProjectId { get; set; } = "solution-compact";
    public string FirebaseWebApiKey { get; set; } = "AIzaSyDvPkzX6B5vmA2VWjZooDW08Pw17mRA29Y";
    public string FirebaseAuthEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/login";
    public string FirebaseHostingUrl { get; set; } = "https://solution-compact.web.app";
    public string FirebasePublishEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/publishWeekPlan";
    public string FirebaseUserSyncEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/syncOnlineAccess";
    public DateTime? LastOnlineAccessSyncAtUtc { get; set; }
    public DateTime? LastOnlineWeekPreparedAtUtc { get; set; }
    public string LastOnlineWeekPreparedId { get; set; } = string.Empty;

    // Trial, registration and seller-managed licensing.
    public DateTime? TrialStartedAtUtc { get; set; }
    public string LicenseInstallationId { get; set; } = string.Empty;
    public string LicenseSecret { get; set; } = string.Empty;
    public string LicenseEmail { get; set; } = string.Empty;
    public string LicenseStatus { get; set; } = string.Empty;
    public DateTime? LicenseValidUntilUtc { get; set; }
    public DateTime? LicenseLastCheckedAtUtc { get; set; }
    public string LicenseRequestEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/licenseRequest";
    public string LicenseStatusEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/licenseStatus";
    public string LicenseRecoverySyncEndpoint { get; set; } = "https://europe-west1-solution-compact.cloudfunctions.net/licenseRecoverySync";

    // Automatic update channel for installed Windows versions.
    public bool AutoUpdateEnabled { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = "https://raw.githubusercontent.com/Happycupra/KPI-rai/main/downloads/update.json";

    // Security settings are installation-wide.
    public bool AutoLockEnabled { get; set; } = true;
    public int AutoLockMinutes { get; set; } = 30;

    // Optional device-local quick access. The four-digit PIN is never stored in plaintext.
    public bool RememberLoginEnabled { get; set; }
    public int? RememberedUserId { get; set; }
    public string RememberedUsername { get; set; } = string.Empty;
    public string QuickAccessPinHash { get; set; } = string.Empty;
    public string QuickAccessPinSalt { get; set; } = string.Empty;
    public DateTime? RememberLoginConfiguredAtUtc { get; set; }

    // Local recovery credential. The PBKDF2 hash/salt remains the local verifier.
    // A freshly generated code is held here only until it has been securely synchronized
    // to the private vendor license record, then this pending plaintext value is cleared.
    public string RecoveryCodeHash { get; set; } = string.Empty;
    public string RecoveryCodeSalt { get; set; } = string.Empty;
    public DateTime? RecoveryCodeCreatedAtUtc { get; set; }
    public string RecoveryCodePendingSupportSync { get; set; } = string.Empty;
    public DateTime? RecoveryCodeLastSupportSyncAtUtc { get; set; }

    // Legacy installation-wide UI values. They remain for backwards-compatible deserialization
    // and are copied into a user's profile the first time that user opens the new version.
    public bool SidebarCollapsed { get; set; }
    public bool PlanningGroupCollapsed { get; set; }
    public bool ProductionGroupCollapsed { get; set; }
    public bool MasterDataGroupCollapsed { get; set; }
    public bool SystemGroupCollapsed { get; set; }
    public int CalendarSelectedViewIndex { get; set; } = 1;
    public string CalendarSearchText { get; set; } = string.Empty;
    public bool CalendarShowAssignments { get; set; } = true;
    public bool CalendarShowOrders { get; set; } = true;
    public bool CalendarShowAbsences { get; set; } = true;
    public bool CalendarShowOperatingCalendar { get; set; } = true;
    public bool CalendarShowWeekends { get; set; } = true;

    public Dictionary<string, UserUiPreferences> UserUiPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UserUiPreferences
{
    public bool SidebarCollapsed { get; set; }
    public bool PlanningGroupCollapsed { get; set; }
    public bool ProductionGroupCollapsed { get; set; }
    public bool MasterDataGroupCollapsed { get; set; }
    public bool SystemGroupCollapsed { get; set; }
    public int CalendarSelectedViewIndex { get; set; } = 1;
    public string CalendarSearchText { get; set; } = string.Empty;
    public bool CalendarShowAssignments { get; set; } = true;
    public bool CalendarShowOrders { get; set; } = true;
    public bool CalendarShowAbsences { get; set; } = true;
    public bool CalendarShowOperatingCalendar { get; set; } = true;
    public bool CalendarShowWeekends { get; set; } = true;
    public bool ShowContextHints { get; set; } = true;
    public int AppTourLastShownVersion { get; set; }
    public int AppTourCompletedVersion { get; set; }
}

public static class AppSettingsService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        lock (Sync)
        {
            return LoadUnlocked();
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Sync)
        {
            SaveUnlocked(settings);
        }
    }

    public static AppSettings Update(Action<AppSettings> update)
    {
        lock (Sync)
        {
            var settings = LoadUnlocked();
            update(settings);
            SaveUnlocked(settings);
            return settings;
        }
    }

    public static UserUiPreferences LoadCurrentUserPreferences()
    {
        lock (Sync)
        {
            var settings = LoadUnlocked();
            var key = GetCurrentUserKey();
            if (!settings.UserUiPreferences.TryGetValue(key, out var preferences))
            {
                preferences = CreateLegacyPreferences(settings);
                settings.UserUiPreferences[key] = preferences;
                SaveUnlocked(settings);
            }
            return Clone(preferences);
        }
    }

    public static UserUiPreferences UpdateCurrentUserPreferences(Action<UserUiPreferences> update)
    {
        lock (Sync)
        {
            var settings = LoadUnlocked();
            var key = GetCurrentUserKey();
            if (!settings.UserUiPreferences.TryGetValue(key, out var preferences))
            {
                preferences = CreateLegacyPreferences(settings);
                settings.UserUiPreferences[key] = preferences;
            }
            update(preferences);
            Normalize(preferences);
            SaveUnlocked(settings);
            return Clone(preferences);
        }
    }

    public static string ResolveStoragePath(string? configured, string fallbackRelative)
    {
        var raw = string.IsNullOrWhiteSpace(configured) ? fallbackRelative : configured.Trim();
        if (Path.IsPathRooted(raw))
        {
            if (!AppPaths.IsPortableMode)
                return raw;
            var root = Path.GetPathRoot(raw);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return raw;
            return Path.Combine(AppPaths.RootDirectory, fallbackRelative);
        }
        return Path.GetFullPath(Path.Combine(AppPaths.RootDirectory, raw));
    }

    public static string ToStoredStoragePath(string? path, string fallbackRelative)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppPaths.IsPortableMode ? fallbackRelative : Path.Combine(AppPaths.RootDirectory, fallbackRelative);

        var full = Path.GetFullPath(path.Trim());
        if (AppPaths.IsPortableMode)
        {
            var root = Path.GetFullPath(AppPaths.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(AppPaths.RootDirectory, full);
        }
        return full;
    }

    private static AppSettings LoadUnlocked()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SettingsPath))
            return NewDefaults();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions) ?? NewDefaults();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return NewDefaults();
        }
    }

    private static void SaveUnlocked(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        Normalize(settings);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static AppSettings NewDefaults() => new()
    {
        CompanyName = "SolutionCompakt",
        DefaultBackupDirectory = AppPaths.IsPortableMode ? "Backups" : AppPaths.BackupsDirectory,
        DefaultExportDirectory = AppPaths.IsPortableMode ? "Exports" : AppPaths.ExportsDirectory
    };

    private static string GetCurrentUserKey()
    {
        var username = SessionService.CurrentUser?.Username?.Trim();
        return string.IsNullOrWhiteSpace(username) ? "__default__" : username.ToLowerInvariant();
    }

    private static UserUiPreferences CreateLegacyPreferences(AppSettings settings) => new()
    {
        SidebarCollapsed = settings.SidebarCollapsed,
        PlanningGroupCollapsed = settings.PlanningGroupCollapsed,
        ProductionGroupCollapsed = settings.ProductionGroupCollapsed,
        MasterDataGroupCollapsed = settings.MasterDataGroupCollapsed,
        SystemGroupCollapsed = settings.SystemGroupCollapsed,
        CalendarSelectedViewIndex = settings.CalendarSelectedViewIndex,
        CalendarSearchText = settings.CalendarSearchText,
        CalendarShowAssignments = settings.CalendarShowAssignments,
        CalendarShowOrders = settings.CalendarShowOrders,
        CalendarShowAbsences = settings.CalendarShowAbsences,
        CalendarShowOperatingCalendar = settings.CalendarShowOperatingCalendar,
        CalendarShowWeekends = settings.CalendarShowWeekends,
        ShowContextHints = true,
        AppTourLastShownVersion = 0,
        AppTourCompletedVersion = 0
    };

    private static UserUiPreferences Clone(UserUiPreferences source) => new()
    {
        SidebarCollapsed = source.SidebarCollapsed,
        PlanningGroupCollapsed = source.PlanningGroupCollapsed,
        ProductionGroupCollapsed = source.ProductionGroupCollapsed,
        MasterDataGroupCollapsed = source.MasterDataGroupCollapsed,
        SystemGroupCollapsed = source.SystemGroupCollapsed,
        CalendarSelectedViewIndex = source.CalendarSelectedViewIndex,
        CalendarSearchText = source.CalendarSearchText,
        CalendarShowAssignments = source.CalendarShowAssignments,
        CalendarShowOrders = source.CalendarShowOrders,
        CalendarShowAbsences = source.CalendarShowAbsences,
        CalendarShowOperatingCalendar = source.CalendarShowOperatingCalendar,
        CalendarShowWeekends = source.CalendarShowWeekends,
        ShowContextHints = source.ShowContextHints,
        AppTourLastShownVersion = source.AppTourLastShownVersion,
        AppTourCompletedVersion = source.AppTourCompletedVersion
    };

    private static void Normalize(AppSettings settings)
    {
        var company = settings.CompanyName?.Trim() ?? string.Empty;
        settings.CompanyName = string.IsNullOrWhiteSpace(company) ||
                               string.Equals(company, "OpsCompact", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(company, "KPI-rai", StringComparison.OrdinalIgnoreCase)
            ? "SolutionCompakt"
            : company;
        settings.CompanyId = settings.CompanyId?.Trim() ?? string.Empty;
        settings.CompanyCode = settings.CompanyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.CompanyRegistrationMode = settings.CompanyRegistrationMode?.Trim() ?? string.Empty;
        settings.SiteName = settings.SiteName?.Trim() ?? string.Empty;
        settings.DefaultBackupDirectory = ToStoredStoragePath(settings.DefaultBackupDirectory, "Backups");
        settings.DefaultExportDirectory = ToStoredStoragePath(settings.DefaultExportDirectory, "Exports");
        settings.BackupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 1, 100);
        settings.CsvDelimiter = string.IsNullOrEmpty(settings.CsvDelimiter) ? ";" : settings.CsvDelimiter[..1];
        settings.LastSuccessfulBackupPath = settings.LastSuccessfulBackupPath?.Trim() ?? string.Empty;
        settings.FirebaseProjectId = settings.FirebaseProjectId?.Trim() ?? string.Empty;
        settings.FirebaseWebApiKey = settings.FirebaseWebApiKey?.Trim() ?? string.Empty;
        settings.FirebaseAuthEndpoint = settings.FirebaseAuthEndpoint?.Trim() ?? string.Empty;
        settings.FirebaseHostingUrl = settings.FirebaseHostingUrl?.Trim() ?? string.Empty;
        settings.FirebasePublishEndpoint = settings.FirebasePublishEndpoint?.Trim() ?? string.Empty;
        settings.FirebaseUserSyncEndpoint = string.IsNullOrWhiteSpace(settings.FirebaseUserSyncEndpoint)
            ? "https://europe-west1-solution-compact.cloudfunctions.net/syncOnlineAccess"
            : settings.FirebaseUserSyncEndpoint.Trim();
        settings.LastOnlineWeekPreparedId = settings.LastOnlineWeekPreparedId?.Trim() ?? string.Empty;
        settings.LicenseInstallationId = settings.LicenseInstallationId?.Trim() ?? string.Empty;
        settings.LicenseSecret = settings.LicenseSecret?.Trim() ?? string.Empty;
        settings.LicenseEmail = settings.LicenseEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        settings.LicenseStatus = settings.LicenseStatus?.Trim().ToLowerInvariant() ?? string.Empty;
        settings.LicenseRequestEndpoint = string.IsNullOrWhiteSpace(settings.LicenseRequestEndpoint)
            ? "https://europe-west1-solution-compact.cloudfunctions.net/licenseRequest"
            : settings.LicenseRequestEndpoint.Trim();
        settings.LicenseStatusEndpoint = string.IsNullOrWhiteSpace(settings.LicenseStatusEndpoint)
            ? "https://europe-west1-solution-compact.cloudfunctions.net/licenseStatus"
            : settings.LicenseStatusEndpoint.Trim();
        settings.LicenseRecoverySyncEndpoint = string.IsNullOrWhiteSpace(settings.LicenseRecoverySyncEndpoint)
            ? "https://europe-west1-solution-compact.cloudfunctions.net/licenseRecoverySync"
            : settings.LicenseRecoverySyncEndpoint.Trim();
        settings.UpdateManifestUrl = string.IsNullOrWhiteSpace(settings.UpdateManifestUrl)
            ? "https://raw.githubusercontent.com/Happycupra/KPI-rai/main/downloads/update.json"
            : settings.UpdateManifestUrl.Trim();
        settings.AutoLockMinutes = Math.Clamp(settings.AutoLockMinutes, 1, 240);
        settings.RecoveryCodeHash = settings.RecoveryCodeHash?.Trim() ?? string.Empty;
        settings.RecoveryCodeSalt = settings.RecoveryCodeSalt?.Trim() ?? string.Empty;
        settings.RecoveryCodePendingSupportSync = settings.RecoveryCodePendingSupportSync?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.CalendarSelectedViewIndex = Math.Clamp(settings.CalendarSelectedViewIndex, 0, 2);
        settings.CalendarSearchText ??= string.Empty;

        settings.UserUiPreferences ??= new Dictionary<string, UserUiPreferences>(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, UserUiPreferences>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.UserUiPreferences)
        {
            var key = pair.Key?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key) || pair.Value is null)
                continue;
            Normalize(pair.Value);
            normalized[key] = pair.Value;
        }
        settings.UserUiPreferences = normalized;
    }

    private static void Normalize(UserUiPreferences preferences)
    {
        preferences.CalendarSelectedViewIndex = Math.Clamp(preferences.CalendarSelectedViewIndex, 0, 2);
        preferences.CalendarSearchText ??= string.Empty;
        preferences.AppTourLastShownVersion = Math.Max(0, preferences.AppTourLastShownVersion);
        preferences.AppTourCompletedVersion = Math.Max(0, preferences.AppTourCompletedVersion);
    }
}

using System.IO;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed class CentralModeSettings
{
    public bool Enabled { get; set; }
    public string DatabaseConnectionString { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = "http://localhost:8088";
    public bool AutoMigrateLocalData { get; set; } = true;
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}

public static class CentralModeService
{
    public const string CentralJoinRegistrationMode = "CentralJoin";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigPath => Path.Combine(AppPaths.RootDirectory, "central-mode.json");

    public static CentralModeSettings Load()
    {
        AppPaths.EnsureDirectories();
        CentralModeSettings settings;
        try
        {
            settings = File.Exists(ConfigPath)
                ? JsonSerializer.Deserialize<CentralModeSettings>(File.ReadAllText(ConfigPath), JsonOptions) ?? new CentralModeSettings()
                : new CentralModeSettings();
        }
        catch
        {
            settings = new CentralModeSettings();
        }

        var enabled = Environment.GetEnvironmentVariable("SOLUTIONCOMPAKT_CENTRAL_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled))
            settings.Enabled = enabled is "1" or "true" or "TRUE" or "yes" or "YES";

        var connection = Environment.GetEnvironmentVariable("SOLUTIONCOMPAKT_CENTRAL_DB");
        if (!string.IsNullOrWhiteSpace(connection))
            settings.DatabaseConnectionString = connection.Trim();

        var server = Environment.GetEnvironmentVariable("SOLUTIONCOMPAKT_CENTRAL_SERVER");
        if (!string.IsNullOrWhiteSpace(server))
            settings.ServerUrl = server.Trim();

        settings.DatabaseConnectionString = settings.DatabaseConnectionString?.Trim() ?? string.Empty;
        settings.ServerUrl = string.IsNullOrWhiteSpace(settings.ServerUrl)
            ? "http://localhost:8088"
            : settings.ServerUrl.Trim().TrimEnd('/');
        settings.CompanyId = settings.CompanyId?.Trim() ?? string.Empty;
        settings.CompanyCode = settings.CompanyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        settings.CompanyName = settings.CompanyName?.Trim() ?? string.Empty;
        return settings;
    }

    public static bool IsEnabled => Load().Enabled;

    public static void EnsureTemplateExists()
    {
        AppPaths.EnsureDirectories();
        if (File.Exists(ConfigPath)) return;
        var template = new CentralModeSettings
        {
            Enabled = false,
            DatabaseConnectionString = "Host=localhost;Port=5432;Database=solutioncompakt;Username=solutioncompakt;Password=CHANGE-ME;SSL Mode=Disable",
            ServerUrl = "http://localhost:8088",
            AutoMigrateLocalData = true
        };
        Save(template);
    }

    public static CentralModeSettings RequireEnabledSettings()
    {
        var settings = Load();
        if (!settings.Enabled)
            throw new InvalidOperationException("Der zentrale Mehrbenutzerbetrieb ist nicht aktiviert.");
        if (string.IsNullOrWhiteSpace(settings.DatabaseConnectionString))
            throw new InvalidOperationException($"In {ConfigPath} fehlt DatabaseConnectionString.");
        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var server) ||
            (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("ServerUrl für den zentralen Mehrbenutzerbetrieb ist ungültig.");
        return settings;
    }

    public static void EnsureLocalCompanyIdentityFromProfile()
    {
        var central = RequireEnabledSettings();
        var local = AppSettingsService.Load();

        if (!string.IsNullOrWhiteSpace(local.CompanyId))
        {
            if (!string.IsNullOrWhiteSpace(central.CompanyId) &&
                !string.Equals(local.CompanyId, central.CompanyId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Das Zentralprofil gehört zu einer anderen Firma als diese Installation.");
            return;
        }

        if (!Guid.TryParse(central.CompanyId, out var companyId) || companyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(central.CompanyCode))
        {
            throw new InvalidOperationException(
                "Diese frische Installation kann der zentralen Firma noch nicht beitreten. Kopieren Sie das central-mode.json einer bereits verbundenen Installation oder tragen Sie CompanyId und CompanyCode in das Zentralprofil ein.");
        }

        AppSettingsService.Update(value =>
        {
            value.CompanyId = companyId.ToString("N");
            value.CompanyCode = central.CompanyCode;
            value.CompanyName = string.IsNullOrWhiteSpace(central.CompanyName) ? "SolutionCompakt" : central.CompanyName;
            value.CompanyRegistrationMode = CentralJoinRegistrationMode;
            value.CompanyRegisteredAtUtc ??= DateTime.UtcNow;
        });
    }

    public static void UpdateProfileFromLocalIdentity()
    {
        if (!File.Exists(ConfigPath)) return;
        var profile = Load();
        var local = AppSettingsService.Load();
        if (!Guid.TryParse(local.CompanyId, out var companyId) || companyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(local.CompanyCode))
            return;

        profile.CompanyId = companyId.ToString("N");
        profile.CompanyCode = local.CompanyCode.Trim().ToUpperInvariant();
        profile.CompanyName = local.CompanyName.Trim();
        Save(profile);
    }

    public static string GetCompanySchemaName()
    {
        var localId = AppSettingsService.Load().CompanyId?.Trim();
        var profileId = Load().CompanyId;
        var raw = string.IsNullOrWhiteSpace(localId) ? profileId : localId;
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id))
            throw new InvalidOperationException("Für den Zentralbetrieb fehlt eine gültige CompanyId.");
        return "company_" + id.ToString("N");
    }

    private static void Save(CentralModeSettings settings)
    {
        AppPaths.EnsureDirectories();
        var pending = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(pending, ConfigPath, true);
        }
        finally
        {
            try { File.Delete(pending); } catch { }
        }
    }
}

using System.IO;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed class CentralModeSettings
{
    public bool Enabled { get; set; }
    public string DatabaseConnectionString { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = "http://localhost:8088";
    public bool AutoMigrateLocalData { get; set; } = true;
}

public static class CentralModeService
{
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
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(template, JsonOptions));
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

    public static string GetCompanySchemaName()
    {
        var companyId = AppSettingsService.Load().CompanyId?.Trim();
        if (string.IsNullOrWhiteSpace(companyId) || !Guid.TryParse(companyId, out var id))
            throw new InvalidOperationException("Vor Aktivierung des Zentralbetriebs muss die lokale Firmenregistrierung vollständig abgeschlossen sein.");
        return "company_" + id.ToString("N");
    }
}

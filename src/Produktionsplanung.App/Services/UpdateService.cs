using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string DownloadUrl,
    string Sha256,
    string Message);

public static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsService.Load();
        try
        {
            using var response = await Http.GetAsync(settings.UpdateManifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);

            if (manifest is null || !Version.TryParse(manifest.Version, out var latest) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                return new UpdateCheckResult(false, false, CurrentVersion, null, string.Empty, string.Empty, "Update-Manifest ist ungültig.");

            var available = latest > CurrentVersion;
            return new UpdateCheckResult(
                true,
                available,
                CurrentVersion,
                latest,
                manifest.DownloadUrl,
                manifest.Sha256 ?? string.Empty,
                available ? $"Version {latest.ToString(3)} ist verfügbar." : "SolutionCompakt ist aktuell.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, CurrentVersion, null, string.Empty, string.Empty,
                "Update-Prüfung nicht möglich: " + ex.Message);
        }
    }

    public static async Task<string> DownloadAndLaunchAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        if (!update.UpdateAvailable || update.LatestVersion is null)
            throw new InvalidOperationException("Es ist kein Update verfügbar.");
        if (AppPaths.IsPortableMode)
            throw new InvalidOperationException("Der automatische Installer-Update ist im USB-/Portable-Modus deaktiviert.");

        var root = Path.Combine(Path.GetTempPath(), "SolutionCompakt-Update", update.LatestVersion.ToString(3));
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        var zipPath = Path.Combine(root, "SolutionCompakt-Setup.zip");
        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(zipPath);
            await input.CopyToAsync(output, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var stream = File.OpenRead(zipPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actual, update.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException("Die Prüfsumme des Updates stimmt nicht. Installation wurde abgebrochen.");
        }

        var extract = Path.Combine(root, "installer");
        ZipFile.ExtractToDirectory(zipPath, extract, overwriteFiles: true);
        var installer = Directory.EnumerateFiles(extract, "SolutionCompakt-Setup-*.exe", SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(extract, "*.exe", SearchOption.AllDirectories).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(installer))
            throw new FileNotFoundException("Der SolutionCompakt-Installer wurde im Update-Paket nicht gefunden.");

        Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
        return installer;
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
    }
}

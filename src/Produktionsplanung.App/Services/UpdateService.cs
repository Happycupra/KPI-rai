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
            if (!IsHttpsUrl(settings.UpdateManifestUrl))
                throw new InvalidDataException("Update-Adresse muss HTTPS verwenden.");
            using var response = await Http.GetAsync(settings.UpdateManifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);

            if (manifest is null || !Version.TryParse(manifest.Version, out var latest) || !IsValidDownload(manifest.DownloadUrl, manifest.Sha256))
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

        if (!IsValidDownload(update.DownloadUrl, update.Sha256))
            throw new InvalidDataException("Update benötigt eine HTTPS-Adresse und eine gültige SHA-256-Prüfsumme.");

        var root = Path.Combine(Path.GetTempPath(), "SolutionCompakt-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var zipPath = Path.Combine(root, "SolutionCompakt-Setup.zip");
        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(zipPath);
            await input.CopyToAsync(output, cancellationToken);
        }

        {
            await using var stream = File.OpenRead(zipPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actual, update.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException("Die Prüfsumme des Updates stimmt nicht. Installation wurde abgebrochen.");
        }

        var extract = Path.Combine(root, "installer");
        ZipFile.ExtractToDirectory(zipPath, extract, overwriteFiles: true);
        var installers = Directory.GetFiles(extract, "SolutionCompakt-Setup-*.exe", SearchOption.AllDirectories);
        var installer = installers.Length == 1 ? installers[0] : null;

        if (string.IsNullOrWhiteSpace(installer))
            throw new FileNotFoundException("Der SolutionCompakt-Installer wurde im Update-Paket nicht gefunden.");

        Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
        return installer;
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo);

    internal static bool IsValidDownload(string? url, string? sha256) =>
        IsHttpsUrl(url) && sha256 is { Length: 64 } && sha256.All(Uri.IsHexDigit);

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
    }
}

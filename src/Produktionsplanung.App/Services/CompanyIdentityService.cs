using System.Text;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class CompanyIdentityService
{
    public const string LocalRegistrationMode = "LocalOffline";
    public const string LegacyMigrationMode = "LegacyMigration";
    public const string SellerCloudMode = "SellerCloud";
    public const string SellerOfflinePackageMode = "SellerOfflinePackage";

    public static AppSettings EnsureExistingInstallationIdentity()
    {
        var settings = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.CompanyId) &&
            !string.IsNullOrWhiteSpace(settings.CompanyCode))
            return settings;

        using var db = new AppDbContext();
        if (!db.UserAccounts.AsNoTracking().Any())
            return settings;

        return AppSettingsService.Update(value =>
        {
            value.CompanyId = string.IsNullOrWhiteSpace(value.CompanyId)
                ? Guid.NewGuid().ToString("N")
                : value.CompanyId;
            value.CompanyCode = string.IsNullOrWhiteSpace(value.CompanyCode)
                ? BuildUniqueLocalCode(value.CompanyName)
                : NormalizeCode(value.CompanyCode);
            if (string.IsNullOrWhiteSpace(value.CompanyRegistrationMode))
                value.CompanyRegistrationMode = LegacyMigrationMode;
            value.CompanyRegisteredAtUtc ??= DateTime.UtcNow;
        });
    }

    public static (bool Success, string Message, AppSettings? Settings) RegisterLocalCompany(
        string companyName,
        string companyCode)
    {
        using var db = new AppDbContext();
        if (db.UserAccounts.AsNoTracking().Any())
            return (false, "Die Firmenregistrierung ist nach der Ersteinrichtung gesperrt.", null);

        var name = (companyName ?? string.Empty).Trim();
        if (name.Length < 2)
            return (false, "Bitte einen gültigen Firmennamen eingeben.", null);

        var code = NormalizeCode(companyCode);
        if (code.Length < 3 || code.Length > 24)
            return (false, "Der Firmen-Code muss 3 bis 24 Zeichen lang sein.", null);
        if (code.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
            return (false, "Der Firmen-Code darf nur Buchstaben, Zahlen und Bindestriche enthalten.", null);

        var current = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(current.CompanyId) &&
            !string.IsNullOrWhiteSpace(current.CompanyCode) &&
            !string.Equals(current.CompanyRegistrationMode, LocalRegistrationMode, StringComparison.Ordinal))
        {
            return (false, "Diese Installation ist bereits einer Firma zugeordnet.", null);
        }

        var settings = AppSettingsService.Update(value =>
        {
            value.CompanyName = name;
            value.CompanyId = string.IsNullOrWhiteSpace(value.CompanyId)
                ? Guid.NewGuid().ToString("N")
                : value.CompanyId;
            value.CompanyCode = code;
            value.CompanyRegistrationMode = LocalRegistrationMode;
            value.CompanyRegisteredAtUtc = DateTime.UtcNow;
        });
        return (true, "Firma wurde lokal registriert.", settings);
    }

    public static bool IsRegistered()
    {
        var settings = AppSettingsService.Load();
        return !string.IsNullOrWhiteSpace(settings.CompanyId) &&
               !string.IsNullOrWhiteSpace(settings.CompanyCode) &&
               !string.IsNullOrWhiteSpace(settings.CompanyName);
    }

    public static string NormalizeCode(string? value)
    {
        var source = (value ?? string.Empty).Trim().ToUpperInvariant();
        var builder = new StringBuilder(source.Length);
        var previousDash = false;
        foreach (var c in source)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    public static string SuggestCode(string? companyName)
    {
        var baseCode = NormalizeCode(companyName);
        if (string.IsNullOrWhiteSpace(baseCode))
            return string.Empty;
        if (baseCode.Length > 18)
            baseCode = baseCode[..18].Trim('-');
        return baseCode;
    }

    private static string BuildUniqueLocalCode(string? companyName)
    {
        var prefix = SuggestCode(companyName);
        if (string.IsNullOrWhiteSpace(prefix) || string.Equals(prefix, "SOLUTIONCOMPAKT", StringComparison.Ordinal))
            prefix = "SC";
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"{prefix}-{suffix}";
    }
}

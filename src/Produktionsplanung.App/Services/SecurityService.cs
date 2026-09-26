using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class SessionService
{
    public static UserAccount? CurrentUser { get; private set; }
    public static bool RequiresRestart { get; private set; }
    public static void InvalidateAfterRestore() { SignOut(); RequiresRestart = true; }
    public static bool IsAuthenticated => CurrentUser is not null;
    public static bool IsAdministrator => CurrentUser?.Role == UserRoles.Administrator;
    public static bool IsPlannerOrAdmin => CurrentUser?.Role is UserRoles.Administrator or UserRoles.Planner;
    public static bool IsObserver => CurrentUser?.Role == UserRoles.Observer;

    public static void SignIn(UserAccount user) => CurrentUser = user;
    public static void SignOut() => CurrentUser = null;
}

public static class PasswordService
{
    private const int Iterations = 150_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hashBase64, string saltBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static string? ValidatePassword(string password)
    {
        if (password.Length < 10) return "Das Passwort muss mindestens 10 Zeichen lang sein.";
        if (!password.Any(char.IsUpper)) return "Das Passwort muss mindestens einen Grossbuchstaben enthalten.";
        if (!password.Any(char.IsLower)) return "Das Passwort muss mindestens einen Kleinbuchstaben enthalten.";
        if (!password.Any(char.IsDigit)) return "Das Passwort muss mindestens eine Zahl enthalten.";
        return null;
    }
}

public static class QuickAccessService
{
    public static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsDigit);

    public static string? ValidatePinPair(string? pin, string? confirmation)
    {
        if (!IsValidPin(pin))
            return "Der Zugangs-PIN muss genau 4 Ziffern enthalten.";
        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
            return "Die beiden PIN-Eingaben stimmen nicht überein.";
        return null;
    }

    public static UserAccount? GetRememberedUser()
    {
        var settings = AppSettingsService.Load();
        if (!settings.RememberLoginEnabled ||
            !settings.RememberedUserId.HasValue ||
            string.IsNullOrWhiteSpace(settings.QuickAccessPinHash) ||
            string.IsNullOrWhiteSpace(settings.QuickAccessPinSalt))
            return null;

        using var db = new AppDbContext();
        var user = db.UserAccounts.AsNoTracking().FirstOrDefault(x => x.Id == settings.RememberedUserId.Value);
        if (user is null || !user.IsActive)
        {
            Clear();
            return null;
        }

        return user;
    }

    public static (bool Success, string Message, UserAccount? User) LoginWithPin(string pin)
    {
        var settings = AppSettingsService.Load();
        var remembered = GetRememberedUser();
        if (remembered is null)
            return (false, "Es ist kein gespeicherter PIN-Zugang eingerichtet.", null);
        if (!IsValidPin(pin) ||
            !PasswordService.Verify(pin, settings.QuickAccessPinHash, settings.QuickAccessPinSalt))
            return (false, "Der Zugangs-PIN ist nicht korrekt.", null);

        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(x => x.Id == remembered.Id && x.IsActive);
        if (user is null)
        {
            Clear();
            return (false, "Das gespeicherte Benutzerkonto ist nicht mehr verfügbar.", null);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        SessionService.SignIn(user);
        AuditService.Log("PIN-Login", "Session", user.Id.ToString(), "Gerätelokaler 4-stelliger Zugangs-PIN.");
        return (true, "Anmeldung mit PIN erfolgreich.", user);
    }

    public static bool CanUseForCurrentUser()
    {
        var current = SessionService.CurrentUser;
        var settings = AppSettingsService.Load();
        return current is not null &&
               settings.RememberLoginEnabled &&
               settings.RememberedUserId == current.Id &&
               !string.IsNullOrWhiteSpace(settings.QuickAccessPinHash) &&
               !string.IsNullOrWhiteSpace(settings.QuickAccessPinSalt);
    }

    public static bool VerifyCurrentPin(string pin)
    {
        if (!CanUseForCurrentUser() || !IsValidPin(pin))
            return false;
        var settings = AppSettingsService.Load();
        return PasswordService.Verify(pin, settings.QuickAccessPinHash, settings.QuickAccessPinSalt);
    }

    public static (bool Success, string Message) Configure(UserAccount user, string pin, string confirmation)
    {
        var validation = ValidatePinPair(pin, confirmation);
        if (validation is not null)
            return (false, validation);

        var (hash, salt) = PasswordService.HashPassword(pin);
        AppSettingsService.Update(settings =>
        {
            settings.RememberLoginEnabled = true;
            settings.RememberedUserId = user.Id;
            settings.RememberedUsername = user.Username;
            settings.QuickAccessPinHash = hash;
            settings.QuickAccessPinSalt = salt;
            settings.RememberLoginConfiguredAtUtc = DateTime.UtcNow;
        });
        AuditService.Log("PIN-Zugang eingerichtet", "Session", user.Id.ToString(), "Gerät bleibt angemeldet; Zugriff ist mit 4-stelligem PIN geschützt.");
        return (true, "PIN-Zugang wurde eingerichtet.");
    }

    public static void ClearIfUser(int userId)
    {
        var settings = AppSettingsService.Load();
        if (settings.RememberedUserId == userId)
            Clear();
    }

    public static void Clear()
    {
        AppSettingsService.Update(settings =>
        {
            settings.RememberLoginEnabled = false;
            settings.RememberedUserId = null;
            settings.RememberedUsername = string.Empty;
            settings.QuickAccessPinHash = string.Empty;
            settings.QuickAccessPinSalt = string.Empty;
            settings.RememberLoginConfiguredAtUtc = null;
        });
    }
}

public static class RecoveryCodeService
{
    public static bool HasRecoveryCode()
    {
        var settings = AppSettingsService.Load();
        return !string.IsNullOrWhiteSpace(settings.RecoveryCodeHash) &&
               !string.IsNullOrWhiteSpace(settings.RecoveryCodeSalt);
    }

    public static string CreateOrReplaceRecoveryCode(bool allowWithoutAuthenticatedAdministrator = false)
    {
        if (!allowWithoutAuthenticatedAdministrator && !SessionService.IsAdministrator)
            throw new InvalidOperationException("Nur ein Administrator darf den Recovery-Code erneuern.");

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(15));
        var normalized = Normalize(raw);
        var display = string.Join('-', Enumerable.Range(0, 6).Select(i => normalized.Substring(i * 5, 5)));
        var (hash, salt) = PasswordService.HashPassword(normalized);
        AppSettingsService.Update(settings =>
        {
            settings.RecoveryCodeHash = hash;
            settings.RecoveryCodeSalt = salt;
            settings.RecoveryCodeCreatedAtUtc = DateTime.UtcNow;
        });
        LicenseService.QueueRecoveryCodeForSupportSync(display);
        AuditService.Log("Recovery-Code erstellt", "Security", null, "Recovery-Code wurde neu erzeugt.");
        return display;
    }

    public static (bool Success, string Message) ResetPassword(string username, string recoveryCode, string newPassword)
    {
        var validation = PasswordService.ValidatePassword(newPassword);
        if (validation is not null)
            return (false, validation);

        var settings = AppSettingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.RecoveryCodeHash) || string.IsNullOrWhiteSpace(settings.RecoveryCodeSalt))
            return (false, "Für diese Installation wurde noch kein Recovery-Code eingerichtet. Bitte mit einem anderen Administratorkonto anmelden oder ein vorhandenes Backup wiederherstellen.");

        var normalizedCode = Normalize(recoveryCode);
        if (normalizedCode.Length != 30 || !PasswordService.Verify(normalizedCode, settings.RecoveryCodeHash, settings.RecoveryCodeSalt))
            return (false, "Der Recovery-Code ist ungültig.");

        var normalizedUser = username.Trim();
        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(x => x.Username == normalizedUser);
        if (user is null)
            return (false, "Dieser Benutzer ist nicht vorhanden.");
        if (!user.IsActive)
            return (false, "Dieser Benutzer ist deaktiviert. Ein Administrator muss ihn zuerst wieder aktivieren.");

        var (hash, salt) = PasswordService.HashPassword(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.SaveChanges();
        QuickAccessService.ClearIfUser(user.Id);
        AuditService.Log("Passwort wiederhergestellt", nameof(UserAccount), user.Id.ToString(), $"Recovery für {user.Username}");
        OnlineAccessSyncService.QueueSync();
        return (true, "Das Passwort wurde zurückgesetzt. Du kannst dich jetzt mit dem neuen Passwort anmelden.");
    }

    private static string Normalize(string? code) =>
        new((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

public static class AuthenticationService
{
    public static bool HasUsers()
    {
        using var db = new AppDbContext();
        return db.UserAccounts.AsNoTracking().Any();
    }

    public static (bool Success, string Message) CreateInitialAdministrator(
        string companyName,
        string companyCode,
        string username,
        string displayName,
        string password)
    {
        using var db = new AppDbContext();
        if (db.UserAccounts.Any()) return (false, "Die Ersteinrichtung wurde bereits abgeschlossen.");

        var validation = ValidateUsernameAndPassword(username, password);
        if (validation is not null) return (false, validation);

        var company = CompanyIdentityService.RegisterLocalCompany(companyName, companyCode);
        if (!company.Success)
            return (false, company.Message);

        var (hash, salt) = PasswordService.HashPassword(password);
        var user = new UserAccount
        {
            Username = username.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRoles.Administrator,
            IsActive = true
        };
        db.UserAccounts.Add(user);
        db.SaveChanges();
        return (true, $"Firma {company.Settings!.CompanyName} und erster Administrator wurden angelegt.");
    }

    public static (bool Success, string Message, UserAccount? User) Login(string username, string password)
    {
        var normalized = username.Trim();
        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(x => x.Username == normalized);
        if (user is null || !user.IsActive || !PasswordService.Verify(password, user.PasswordHash, user.PasswordSalt))
            return (false, "Benutzername oder Passwort ist ungültig.", null);

        user.LastLoginAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        SessionService.SignIn(user);
        AuditService.Log("Login", "Session", user.Id.ToString(), $"Rolle: {user.Role}");
        return (true, "Anmeldung erfolgreich.", user);
    }

    public static (bool Success, string Message) VerifyCurrentPassword(string password)
    {
        var current = SessionService.CurrentUser;
        if (current is null)
            return (false, "Keine aktive Sitzung.");

        using var db = new AppDbContext();
        var user = db.UserAccounts.AsNoTracking().FirstOrDefault(x => x.Id == current.Id);
        if (user is null || !user.IsActive)
            return (false, "Das aktuelle Benutzerkonto ist nicht verfügbar.");

        return PasswordService.Verify(password, user.PasswordHash, user.PasswordSalt)
            ? (true, "Passwort bestätigt.")
            : (false, "Das aktuelle Passwort ist falsch.");
    }

    public static (bool Success, string Message) ChangeOwnPassword(string currentPassword, string newPassword)
    {
        var current = SessionService.CurrentUser;
        if (current is null) return (false, "Keine aktive Sitzung.");

        var validation = PasswordService.ValidatePassword(newPassword);
        if (validation is not null) return (false, validation);

        using var db = new AppDbContext();
        var user = db.UserAccounts.FirstOrDefault(x => x.Id == current.Id);
        if (user is null || !PasswordService.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
            return (false, "Das aktuelle Passwort ist falsch.");

        var (hash, salt) = PasswordService.HashPassword(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.SaveChanges();
        QuickAccessService.ClearIfUser(user.Id);
        AuditService.Log("Passwort geändert", nameof(UserAccount), user.Id.ToString(), "Eigenes Passwort geändert");
        OnlineAccessSyncService.QueueSync();
        return (true, "Passwort wurde geändert.");
    }

    private static string? ValidateUsernameAndPassword(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
            return "Der Benutzername muss mindestens 3 Zeichen lang sein.";
        return PasswordService.ValidatePassword(password);
    }
}

public static class AuditService
{
    public static void Log(string action, string entityType, string? entityId = null, string? details = null)
    {
        try
        {
            using var db = new AppDbContext();
            db.AuditLogs.Add(new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                Username = SessionService.CurrentUser?.Username ?? "SYSTEM",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details
            });
            db.SaveChanges();
        }
        catch
        {
            // Audit logging must never crash the application.
        }
    }
}

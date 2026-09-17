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
        AuditService.Log("Passwort wiederhergestellt", nameof(UserAccount), user.Id.ToString(), $"Recovery für {user.Username}");
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

    public static (bool Success, string Message) CreateInitialAdministrator(string username, string displayName, string password)
    {
        using var db = new AppDbContext();
        if (db.UserAccounts.Any()) return (false, "Die Ersteinrichtung wurde bereits abgeschlossen.");

        var validation = ValidateUsernameAndPassword(username, password);
        if (validation is not null) return (false, validation);

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
        return (true, "Administrator wurde angelegt.");
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
        AuditService.Log("Passwort geändert", nameof(UserAccount), user.Id.ToString(), "Eigenes Passwort geändert");
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

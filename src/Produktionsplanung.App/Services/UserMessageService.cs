using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class UserMessageService
{
    public static IReadOnlyList<UserMessageRecipient> GetRecipients()
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        return db.UserAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.Id != current.Id)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Username)
            .Select(x => new UserMessageRecipient(x.Id, x.DisplayName, x.Username))
            .ToList();
    }

    public static UserMessage Send(int recipientUserId, string? subject, string body, string priority = "Normal")
    {
        var sender = RequireCurrentUser();
        var normalizedBody = (body ?? string.Empty).Trim();
        if (normalizedBody.Length == 0)
            throw new InvalidOperationException("Bitte einen Hinweistext eingeben.");
        if (normalizedBody.Length > 4000)
            throw new InvalidOperationException("Der Hinweis darf maximal 4000 Zeichen lang sein.");

        var normalizedSubject = string.IsNullOrWhiteSpace(subject) ? "Hinweis" : subject.Trim();
        if (normalizedSubject.Length > 120)
            throw new InvalidOperationException("Der Betreff darf maximal 120 Zeichen lang sein.");

        var normalizedPriority = string.Equals(priority, "Wichtig", StringComparison.OrdinalIgnoreCase)
            ? "Wichtig"
            : "Normal";

        using var db = new AppDbContext();
        var recipient = db.UserAccounts.AsNoTracking().SingleOrDefault(x => x.Id == recipientUserId && x.IsActive)
            ?? throw new InvalidOperationException("Der ausgewählte Empfänger ist nicht mehr aktiv.");

        if (recipient.Id == sender.Id)
            throw new InvalidOperationException("Ein Hinweis kann nicht an das eigene Konto gesendet werden.");

        var message = new UserMessage
        {
            SenderUserId = sender.Id,
            SenderUsernameSnapshot = sender.Username,
            SenderDisplayNameSnapshot = sender.DisplayName,
            RecipientUserId = recipient.Id,
            RecipientUsernameSnapshot = recipient.Username,
            RecipientDisplayNameSnapshot = recipient.DisplayName,
            Subject = normalizedSubject,
            Body = normalizedBody,
            Priority = normalizedPriority,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.UserMessages.Add(message);
        db.SaveChanges();
        return message;
    }

    public static IReadOnlyList<UserMessageRow> GetInbox()
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        return db.UserMessages.AsNoTracking()
            .Where(x => x.RecipientUserId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, false))
            .ToList();
    }

    public static IReadOnlyList<UserMessageRow> GetSent()
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        return db.UserMessages.AsNoTracking()
            .Where(x => x.SenderUserId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, true))
            .ToList();
    }

    public static IReadOnlyList<UserMessageRow> GetUnread()
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        return db.UserMessages.AsNoTracking()
            .Where(x => x.RecipientUserId == current.Id && x.AcknowledgedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, false))
            .ToList();
    }

    public static int GetUnreadCount()
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        return db.UserMessages.AsNoTracking()
            .Count(x => x.RecipientUserId == current.Id && x.AcknowledgedAtUtc == null);
    }

    public static bool Acknowledge(int messageId)
    {
        var current = RequireCurrentUser();
        using var db = new AppDbContext();
        var message = db.UserMessages.SingleOrDefault(x => x.Id == messageId && x.RecipientUserId == current.Id);
        if (message is null)
            return false;
        if (message.AcknowledgedAtUtc.HasValue)
            return true;

        message.AcknowledgedAtUtc = DateTime.UtcNow;
        message.AcknowledgedByUsername = current.Username;
        db.SaveChanges();
        return true;
    }

    private static UserAccount RequireCurrentUser() =>
        SessionService.CurrentUser ?? throw new InvalidOperationException("Für Hinweise ist eine Anmeldung erforderlich.");

    private static UserMessageRow ToRow(UserMessage x, bool sent) => new()
    {
        Id = x.Id,
        Partner = sent ? x.RecipientDisplayNameSnapshot : x.SenderDisplayNameSnapshot,
        PartnerUsername = sent ? x.RecipientUsernameSnapshot : x.SenderUsernameSnapshot,
        Subject = x.Subject,
        Body = x.Body,
        Priority = x.Priority,
        CreatedAtUtc = x.CreatedAtUtc,
        AcknowledgedAtUtc = x.AcknowledgedAtUtc,
        IsSent = sent
    };
}

public sealed record UserMessageRecipient(int Id, string DisplayName, string Username)
{
    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName) || string.Equals(DisplayName, Username, StringComparison.OrdinalIgnoreCase)
        ? Username
        : $"{DisplayName} ({Username})";
}

public sealed class UserMessageRow
{
    public int Id { get; init; }
    public string Partner { get; init; } = string.Empty;
    public string PartnerUsername { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Priority { get; init; } = "Normal";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }
    public bool IsSent { get; init; }
    public bool IsAcknowledged => AcknowledgedAtUtc.HasValue;
    public string CreatedText => CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string StatusText => IsSent
        ? (AcknowledgedAtUtc.HasValue ? $"Gelesen {AcknowledgedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}" : "Noch nicht bestätigt")
        : (AcknowledgedAtUtc.HasValue ? "Gelesen bestätigt" : "Ungelesen");
}

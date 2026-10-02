using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class UserMessageService
{
    private static readonly object CentralUnreadCacheGate = new();
    private static List<UserMessageRow> centralUnreadCache = new();
    private static int centralUnreadCount;

    public static IReadOnlyList<UserMessageRecipient> GetRecipients() =>
        GetRecipientsAsync().GetAwaiter().GetResult();

    public static async Task<IReadOnlyList<UserMessageRecipient>> GetRecipientsAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetRecipientsLocal(current);

        var recipients = await CentralServerClient
            .GetAsync<List<CentralMessageRecipientDto>>("api/v1/messages/recipients", cancellationToken)
            .ConfigureAwait(false);
        return recipients
            .Select(x => new UserMessageRecipient(x.Id, x.DisplayName, x.Username))
            .ToList();
    }

    public static UserMessage Send(int recipientUserId, string? subject, string body, string priority = "Normal") =>
        SendAsync(recipientUserId, subject, body, priority).GetAwaiter().GetResult();

    public static async Task<UserMessage> SendAsync(
        int recipientUserId,
        string? subject,
        string body,
        string priority = "Normal",
        CancellationToken cancellationToken = default)
    {
        var sender = RequireCurrentUser();
        var normalized = NormalizeMessage(subject, body, priority);

        if (!CentralModeService.IsEnabled)
            return SendLocal(sender, recipientUserId, normalized.Subject, normalized.Body, normalized.Priority);

        var message = await CentralServerClient.PostAsync<CentralSendMessageRequest, CentralMessageDto>(
                "api/v1/messages/",
                new CentralSendMessageRequest(recipientUserId, normalized.Subject, normalized.Body, normalized.Priority),
                cancellationToken)
            .ConfigureAwait(false);
        return ToEntity(message);
    }

    public static IReadOnlyList<UserMessageRow> GetInbox() =>
        GetInboxAsync().GetAwaiter().GetResult();

    public static async Task<IReadOnlyList<UserMessageRow>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetInboxLocal(current);

        var messages = await CentralServerClient
            .GetAsync<List<CentralMessageDto>>("api/v1/messages/inbox", cancellationToken)
            .ConfigureAwait(false);
        return messages.Select(x => ToRow(x, false)).ToList();
    }

    public static IReadOnlyList<UserMessageRow> GetSent() =>
        GetSentAsync().GetAwaiter().GetResult();

    public static async Task<IReadOnlyList<UserMessageRow>> GetSentAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetSentLocal(current);

        var messages = await CentralServerClient
            .GetAsync<List<CentralMessageDto>>("api/v1/messages/sent", cancellationToken)
            .ConfigureAwait(false);
        return messages.Select(x => ToRow(x, true)).ToList();
    }

    public static IReadOnlyList<UserMessageRow> GetUnread()
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetUnreadLocal(current);

        lock (CentralUnreadCacheGate)
            return centralUnreadCache.ToList();
    }

    public static async Task<IReadOnlyList<UserMessageRow>> GetUnreadAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetUnreadLocal(current);

        var messages = await CentralServerClient
            .GetAsync<List<CentralMessageDto>>("api/v1/messages/unread", cancellationToken)
            .ConfigureAwait(false);
        var rows = messages.Select(x => ToRow(x, false)).ToList();
        lock (CentralUnreadCacheGate)
        {
            centralUnreadCache = rows;
            centralUnreadCount = rows.Count;
        }
        return rows;
    }

    public static int GetUnreadCount()
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetUnreadCountLocal(current);

        lock (CentralUnreadCacheGate)
            return centralUnreadCount;
    }

    public static async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return GetUnreadCountLocal(current);

        var result = await CentralServerClient
            .GetAsync<CentralMessageCountDto>("api/v1/messages/unread-count", cancellationToken)
            .ConfigureAwait(false);
        lock (CentralUnreadCacheGate)
            centralUnreadCount = result.Count;
        return result.Count;
    }

    public static bool Acknowledge(int messageId) =>
        AcknowledgeAsync(messageId).GetAwaiter().GetResult();

    public static async Task<bool> AcknowledgeAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentUser();
        if (!CentralModeService.IsEnabled)
            return AcknowledgeLocal(current, messageId);

        var result = await CentralServerClient
            .PostAsync<CentralMessageAcknowledgeDto>($"api/v1/messages/{messageId}/acknowledge", cancellationToken)
            .ConfigureAwait(false);
        if (result.Found)
        {
            lock (CentralUnreadCacheGate)
            {
                centralUnreadCache.RemoveAll(x => x.Id == messageId);
                centralUnreadCount = centralUnreadCache.Count;
            }
        }
        return result.Found;
    }

    public static void ResetCentralUnreadCache()
    {
        lock (CentralUnreadCacheGate)
        {
            centralUnreadCache = new List<UserMessageRow>();
            centralUnreadCount = 0;
        }
    }

    private static IReadOnlyList<UserMessageRecipient> GetRecipientsLocal(UserAccount current)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
        return db.UserAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.Id != current.Id)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Username)
            .Select(x => new UserMessageRecipient(x.Id, x.DisplayName, x.Username))
            .ToList();
    }

    private static UserMessage SendLocal(
        UserAccount sender,
        int recipientUserId,
        string subject,
        string body,
        string priority)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
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
            Subject = subject,
            Body = body,
            Priority = priority,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.UserMessages.Add(message);
        db.SaveChanges();
        return message;
    }

    private static IReadOnlyList<UserMessageRow> GetInboxLocal(UserAccount current)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
        return db.UserMessages.AsNoTracking()
            .Where(x => x.RecipientUserId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, false))
            .ToList();
    }

    private static IReadOnlyList<UserMessageRow> GetSentLocal(UserAccount current)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
        return db.UserMessages.AsNoTracking()
            .Where(x => x.SenderUserId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, true))
            .ToList();
    }

    private static IReadOnlyList<UserMessageRow> GetUnreadLocal(UserAccount current)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
        return db.UserMessages.AsNoTracking()
            .Where(x => x.RecipientUserId == current.Id && x.AcknowledgedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .AsEnumerable()
            .Select(x => ToRow(x, false))
            .ToList();
    }

    private static int GetUnreadCountLocal(UserAccount current)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
        return db.UserMessages.AsNoTracking()
            .Count(x => x.RecipientUserId == current.Id && x.AcknowledgedAtUtc == null);
    }

    private static bool AcknowledgeLocal(UserAccount current, int messageId)
    {
        using var db = new AppDbContext(AppDatabaseMode.Local);
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

    private static (string Subject, string Body, string Priority) NormalizeMessage(
        string? subject,
        string? body,
        string? priority)
    {
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
        return (normalizedSubject, normalizedBody, normalizedPriority);
    }

    private static UserAccount RequireCurrentUser() =>
        SessionService.CurrentUser ?? throw new InvalidOperationException("Für Hinweise ist eine Anmeldung erforderlich.");

    private static UserMessage ToEntity(CentralMessageDto x) => new()
    {
        Id = x.Id,
        SenderUserId = x.SenderUserId,
        SenderUsernameSnapshot = x.SenderUsernameSnapshot,
        SenderDisplayNameSnapshot = x.SenderDisplayNameSnapshot,
        RecipientUserId = x.RecipientUserId,
        RecipientUsernameSnapshot = x.RecipientUsernameSnapshot,
        RecipientDisplayNameSnapshot = x.RecipientDisplayNameSnapshot,
        Subject = x.Subject,
        Body = x.Body,
        Priority = x.Priority,
        CreatedAtUtc = x.CreatedAtUtc,
        AcknowledgedAtUtc = x.AcknowledgedAtUtc,
        AcknowledgedByUsername = x.AcknowledgedByUsername
    };

    private static UserMessageRow ToRow(CentralMessageDto x, bool sent) => new()
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

internal sealed record CentralMessageRecipientDto(int Id, string DisplayName, string Username);
internal sealed record CentralSendMessageRequest(int RecipientUserId, string Subject, string Body, string Priority);
internal sealed record CentralMessageCountDto(int Count);
internal sealed record CentralMessageAcknowledgeDto(bool Found, int SenderUserId, DateTime? AcknowledgedAtUtc);
internal sealed record CentralMessageDto(
    int Id,
    int SenderUserId,
    string SenderUsernameSnapshot,
    string SenderDisplayNameSnapshot,
    int RecipientUserId,
    string RecipientUsernameSnapshot,
    string RecipientDisplayNameSnapshot,
    string Subject,
    string Body,
    string Priority,
    DateTime CreatedAtUtc,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedByUsername);

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

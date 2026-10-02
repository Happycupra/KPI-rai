using Npgsql;

namespace SolutionCompakt.Server.Messaging;

public sealed record MessageRecipientDto(int Id, string DisplayName, string Username);

public sealed record MessageDto(
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

public sealed record SendMessageRequest(int RecipientUserId, string? Subject, string? Body, string? Priority);
public sealed record MessageCountDto(int Count);
public sealed record MessageAcknowledgeResult(bool Found, int SenderUserId, DateTime? AcknowledgedAtUtc);

public sealed class CentralMessageStore
{
    private readonly string connectionString;

    public CentralMessageStore(IConfiguration configuration)
    {
        connectionString = configuration.GetConnectionString("CentralDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:CentralDatabase is required.");
    }

    public async Task<IReadOnlyList<MessageRecipientDto>> GetRecipientsAsync(
        Guid companyId,
        int currentUserId,
        CancellationToken cancellationToken)
    {
        var results = new List<MessageRecipientDto>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT "Id", "DisplayName", "Username"
            FROM {Qualified(companyId, "UserAccounts")}
            WHERE "IsActive" = TRUE AND "Id" <> @currentUserId
            ORDER BY "DisplayName", "Username";
            """;
        command.Parameters.AddWithValue("currentUserId", currentUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return results;
    }

    public async Task<MessageDto> SendAsync(
        Guid companyId,
        int senderUserId,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
            throw new MessageValidationException("Bitte einen Hinweistext eingeben.");
        if (body.Length > 4000)
            throw new MessageValidationException("Der Hinweis darf maximal 4000 Zeichen lang sein.");

        var subject = string.IsNullOrWhiteSpace(request.Subject) ? "Hinweis" : request.Subject.Trim();
        if (subject.Length > 120)
            throw new MessageValidationException("Der Betreff darf maximal 120 Zeichen lang sein.");

        var priority = string.Equals(request.Priority, "Wichtig", StringComparison.OrdinalIgnoreCase)
            ? "Wichtig"
            : "Normal";

        if (request.RecipientUserId < 1)
            throw new MessageValidationException("Ungültiger Empfänger.");
        if (request.RecipientUserId == senderUserId)
            throw new MessageValidationException("Ein Hinweis kann nicht an das eigene Konto gesendet werden.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var sender = await GetActiveUserAsync(connection, transaction, companyId, senderUserId, cancellationToken)
            ?? throw new MessageValidationException("Der angemeldete Benutzer ist nicht mehr aktiv.");
        var recipient = await GetActiveUserAsync(connection, transaction, companyId, request.RecipientUserId, cancellationToken)
            ?? throw new MessageValidationException("Der ausgewählte Empfänger ist nicht mehr aktiv.");

        var now = DateTime.UtcNow;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {Qualified(companyId, "UserMessages")}
                ("SenderUserId", "SenderUsernameSnapshot", "SenderDisplayNameSnapshot",
                 "RecipientUserId", "RecipientUsernameSnapshot", "RecipientDisplayNameSnapshot",
                 "Subject", "Body", "Priority", "CreatedAtUtc", "AcknowledgedAtUtc", "AcknowledgedByUsername", "ConcurrencyToken")
            VALUES
                (@senderId, @senderUsername, @senderDisplayName,
                 @recipientId, @recipientUsername, @recipientDisplayName,
                 @subject, @body, @priority, @createdAt, NULL, NULL, @token)
            RETURNING "Id";
            """;
        command.Parameters.AddWithValue("senderId", sender.Id);
        command.Parameters.AddWithValue("senderUsername", sender.Username);
        command.Parameters.AddWithValue("senderDisplayName", sender.DisplayName);
        command.Parameters.AddWithValue("recipientId", recipient.Id);
        command.Parameters.AddWithValue("recipientUsername", recipient.Username);
        command.Parameters.AddWithValue("recipientDisplayName", recipient.DisplayName);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("priority", priority);
        command.Parameters.AddWithValue("createdAt", now);
        command.Parameters.AddWithValue("token", Guid.NewGuid());

        var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            companyId,
            sender.Username,
            "Erstellt",
            id,
            $"Empfänger: {recipient.Username}; Betreff: {subject}; Priorität: {priority}",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MessageDto(
            id,
            sender.Id,
            sender.Username,
            sender.DisplayName,
            recipient.Id,
            recipient.Username,
            recipient.DisplayName,
            subject,
            body,
            priority,
            now,
            null,
            null);
    }

    public Task<IReadOnlyList<MessageDto>> GetInboxAsync(Guid companyId, int userId, CancellationToken cancellationToken) =>
        GetMessagesAsync(companyId, userId, sent: false, unreadOnly: false, cancellationToken);

    public Task<IReadOnlyList<MessageDto>> GetSentAsync(Guid companyId, int userId, CancellationToken cancellationToken) =>
        GetMessagesAsync(companyId, userId, sent: true, unreadOnly: false, cancellationToken);

    public Task<IReadOnlyList<MessageDto>> GetUnreadAsync(Guid companyId, int userId, CancellationToken cancellationToken) =>
        GetMessagesAsync(companyId, userId, sent: false, unreadOnly: true, cancellationToken);

    public async Task<int> GetUnreadCountAsync(Guid companyId, int userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM {Qualified(companyId, "UserMessages")}
            WHERE "RecipientUserId" = @userId AND "AcknowledgedAtUtc" IS NULL;
            """;
        command.Parameters.AddWithValue("userId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<MessageAcknowledgeResult> AcknowledgeAsync(
        Guid companyId,
        int recipientUserId,
        CancellationToken cancellationToken,
        int messageId)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var recipient = await GetActiveUserAsync(connection, transaction, companyId, recipientUserId, cancellationToken)
            ?? throw new MessageValidationException("Der angemeldete Benutzer ist nicht mehr aktiv.");

        var now = DateTime.UtcNow;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {Qualified(companyId, "UserMessages")}
            SET "AcknowledgedAtUtc" = COALESCE("AcknowledgedAtUtc", @now),
                "AcknowledgedByUsername" = COALESCE("AcknowledgedByUsername", @username),
                "ConcurrencyToken" = @token
            WHERE "Id" = @messageId AND "RecipientUserId" = @recipientUserId
            RETURNING "SenderUserId", "AcknowledgedAtUtc";
            """;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("username", recipient.Username);
        command.Parameters.AddWithValue("token", Guid.NewGuid());
        command.Parameters.AddWithValue("messageId", messageId);
        command.Parameters.AddWithValue("recipientUserId", recipientUserId);

        int senderUserId;
        DateTime acknowledgedAtUtc;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(false, 0, null);
            }
            senderUserId = reader.GetInt32(0);
            acknowledgedAtUtc = reader.GetDateTime(1);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            companyId,
            recipient.Username,
            "Geändert",
            messageId,
            "Persönlicher Hinweis als gelesen bestätigt.",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, senderUserId, acknowledgedAtUtc);
    }

    private async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(
        Guid companyId,
        int userId,
        bool sent,
        bool unreadOnly,
        CancellationToken cancellationToken)
    {
        var results = new List<MessageDto>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var directionColumn = sent ? "SenderUserId" : "RecipientUserId";
        var unreadClause = unreadOnly ? " AND \"AcknowledgedAtUtc\" IS NULL" : string.Empty;
        command.CommandText = $"""
            SELECT "Id", "SenderUserId", "SenderUsernameSnapshot", "SenderDisplayNameSnapshot",
                   "RecipientUserId", "RecipientUsernameSnapshot", "RecipientDisplayNameSnapshot",
                   "Subject", "Body", "Priority", "CreatedAtUtc", "AcknowledgedAtUtc", "AcknowledgedByUsername"
            FROM {Qualified(companyId, "UserMessages")}
            WHERE "{directionColumn}" = @userId{unreadClause}
            ORDER BY "CreatedAtUtc" {(unreadOnly ? "ASC" : "DESC")};
            """;
        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MessageDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }
        return results;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<CentralUserSnapshot?> GetActiveUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid companyId,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT "Id", "Username", "DisplayName"
            FROM {Qualified(companyId, "UserAccounts")}
            WHERE "Id" = @userId AND "IsActive" = TRUE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid companyId,
        string username,
        string action,
        int messageId,
        string details,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {Qualified(companyId, "AuditLogs")}
                ("TimestampUtc", "Username", "Action", "EntityType", "EntityId", "Details", "ConcurrencyToken")
            VALUES
                (@timestamp, @username, @action, 'UserMessage', @entityId, @details, @token);
            """;
        command.Parameters.AddWithValue("timestamp", timestampUtc);
        command.Parameters.AddWithValue("username", username);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entityId", messageId.ToString());
        command.Parameters.AddWithValue("details", details);
        command.Parameters.AddWithValue("token", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Qualified(Guid companyId, string table) =>
        $"\"company_{companyId:N}\".\"{table}\"";

    private sealed record CentralUserSnapshot(int Id, string Username, string DisplayName);
}

public sealed class MessageValidationException(string message) : Exception(message);

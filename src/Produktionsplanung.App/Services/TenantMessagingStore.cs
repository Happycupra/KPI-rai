using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record TenantMessageSyncRow(
    int LocalMessageId,
    string OnlineMessageId,
    string CompanyId,
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

public sealed record TenantCloudMessage(
    string OnlineMessageId,
    string CompanyId,
    int? SourceMessageId,
    string SourceInstallationId,
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

public static class TenantMessagingStore
{
    public static void ApplySchemaAndBackfill(AppDbContext db)
    {
        EnsureColumn(db, "UserAccounts", "CompanyId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "UserMessages", "CompanyId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "UserMessages", "OnlineMessageId", "TEXT NULL");

        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_UserAccounts_CompanyId ON UserAccounts (CompanyId);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_UserMessages_CompanyId_RecipientUserId_AcknowledgedAtUtc_CreatedAtUtc ON UserMessages (CompanyId,RecipientUserId,AcknowledgedAtUtc,CreatedAtUtc);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_UserMessages_CompanyId_SenderUserId_CreatedAtUtc ON UserMessages (CompanyId,SenderUserId,CreatedAtUtc);");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserMessages_OnlineMessageId ON UserMessages (OnlineMessageId) WHERE OnlineMessageId IS NOT NULL AND TRIM(OnlineMessageId) <> '';");

        var companyId = AppSettingsService.Load().CompanyId.Trim();
        if (string.IsNullOrWhiteSpace(companyId))
            return;

        db.Database.ExecuteSqlInterpolated($"UPDATE UserAccounts SET CompanyId={companyId} WHERE CompanyId IS NULL OR TRIM(CompanyId)=''");
        db.Database.ExecuteSqlInterpolated($"UPDATE UserMessages SET CompanyId={companyId} WHERE CompanyId IS NULL OR TRIM(CompanyId)=''");
    }

    public static string RequireCompanyId()
    {
        var companyId = AppSettingsService.Load().CompanyId.Trim();
        if (string.IsNullOrWhiteSpace(companyId))
            throw new InvalidOperationException("Diese Installation ist noch keiner Firma zugeordnet.");
        return companyId;
    }

    public static void BindUnassignedUsers(AppDbContext db, string? companyId = null)
    {
        var tenant = string.IsNullOrWhiteSpace(companyId) ? RequireCompanyId() : companyId.Trim();
        db.Database.ExecuteSqlInterpolated($"UPDATE UserAccounts SET CompanyId={tenant} WHERE CompanyId IS NULL OR TRIM(CompanyId)=''");
    }

    public static IQueryable<UserAccount> CompanyUsers(AppDbContext db, string companyId) =>
        db.UserAccounts.FromSqlInterpolated($"SELECT * FROM UserAccounts WHERE CompanyId={companyId}");

    public static IQueryable<UserMessage> CompanyMessages(AppDbContext db, string companyId) =>
        db.UserMessages.FromSqlInterpolated($"SELECT * FROM UserMessages WHERE CompanyId={companyId}");

    public static bool UserBelongsToCompany(AppDbContext db, int userId, string companyId) =>
        CompanyUsers(db, companyId).AsNoTracking().Any(x => x.Id == userId);

    public static void BindMessage(AppDbContext db, int messageId, string companyId)
    {
        db.Database.ExecuteSqlInterpolated($"UPDATE UserMessages SET CompanyId={companyId} WHERE Id={messageId} AND (CompanyId IS NULL OR TRIM(CompanyId)='' OR CompanyId={companyId})");
    }

    public static IReadOnlyList<TenantMessageSyncRow> LoadForSync(AppDbContext db, string companyId)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != System.Data.ConnectionState.Open;
        if (closeAfter) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, COALESCE(OnlineMessageId,''), CompanyId,
                       SenderUserId, SenderUsernameSnapshot, SenderDisplayNameSnapshot,
                       RecipientUserId, RecipientUsernameSnapshot, RecipientDisplayNameSnapshot,
                       Subject, Body, Priority, CreatedAtUtc, AcknowledgedAtUtc, AcknowledgedByUsername
                FROM UserMessages
                WHERE CompanyId=$companyId
                ORDER BY CreatedAtUtc DESC
                LIMIT 500;
                """;
            var p = command.CreateParameter();
            p.ParameterName = "$companyId";
            p.Value = companyId;
            command.Parameters.Add(p);

            using var reader = command.ExecuteReader();
            var rows = new List<TenantMessageSyncRow>();
            while (reader.Read())
            {
                rows.Add(new TenantMessageSyncRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                    reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                    reader.IsDBNull(14) ? null : reader.GetString(14)));
            }
            return rows;
        }
        finally
        {
            if (closeAfter) connection.Close();
        }
    }

    public static void MergeCloudMessages(AppDbContext db, string companyId, string installationId, IReadOnlyList<TenantCloudMessage> messages)
    {
        var knownUsers = CompanyUsers(db, companyId).AsNoTracking().Select(x => x.Id).ToHashSet();
        foreach (var message in messages)
        {
            if (!string.Equals(message.CompanyId, companyId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.OnlineMessageId) ||
                !knownUsers.Contains(message.SenderUserId) ||
                !knownUsers.Contains(message.RecipientUserId))
                continue;

            int? localId = null;
            if (string.Equals(message.SourceInstallationId, installationId, StringComparison.Ordinal) && message.SourceMessageId is > 0)
                localId = message.SourceMessageId;

            if (!localId.HasValue)
            {
                using var lookup = db.Database.GetDbConnection().CreateCommand();
                var connection = lookup.Connection!;
                var closeAfter = connection.State != System.Data.ConnectionState.Open;
                if (closeAfter) connection.Open();
                try
                {
                    lookup.CommandText = "SELECT Id FROM UserMessages WHERE CompanyId=$companyId AND OnlineMessageId=$onlineId LIMIT 1;";
                    AddParameter(lookup, "$companyId", companyId);
                    AddParameter(lookup, "$onlineId", message.OnlineMessageId);
                    var value = lookup.ExecuteScalar();
                    if (value is not null && value != DBNull.Value)
                        localId = Convert.ToInt32(value);
                }
                finally
                {
                    if (closeAfter) connection.Close();
                }
            }

            if (localId.HasValue)
            {
                db.Database.ExecuteSqlInterpolated($"""
                    UPDATE UserMessages
                    SET OnlineMessageId={message.OnlineMessageId}, CompanyId={companyId},
                        AcknowledgedAtUtc={message.AcknowledgedAtUtc}, AcknowledgedByUsername={message.AcknowledgedByUsername}
                    WHERE Id={localId.Value} AND CompanyId={companyId};
                    """);
                continue;
            }

            db.Database.ExecuteSqlInterpolated($"""
                INSERT INTO UserMessages
                    (SenderUserId,SenderUsernameSnapshot,SenderDisplayNameSnapshot,
                     RecipientUserId,RecipientUsernameSnapshot,RecipientDisplayNameSnapshot,
                     Subject,Body,Priority,CreatedAtUtc,AcknowledgedAtUtc,AcknowledgedByUsername,
                     CompanyId,OnlineMessageId)
                VALUES
                    ({message.SenderUserId},{message.SenderUsernameSnapshot},{message.SenderDisplayNameSnapshot},
                     {message.RecipientUserId},{message.RecipientUsernameSnapshot},{message.RecipientDisplayNameSnapshot},
                     {message.Subject},{message.Body},{message.Priority},{message.CreatedAtUtc},
                     {message.AcknowledgedAtUtc},{message.AcknowledgedByUsername},{companyId},{message.OnlineMessageId});
                """);
        }
    }

    public static string? GetOnlineMessageId(AppDbContext db, int localMessageId, string companyId)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != System.Data.ConnectionState.Open;
        if (closeAfter) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT OnlineMessageId FROM UserMessages WHERE Id=$id AND CompanyId=$companyId LIMIT 1;";
            AddParameter(command, "$id", localMessageId);
            AddParameter(command, "$companyId", companyId);
            var value = command.ExecuteScalar();
            return value is null or DBNull ? null : Convert.ToString(value);
        }
        finally
        {
            if (closeAfter) connection.Close();
        }
    }

    private static void EnsureColumn(AppDbContext db, string table, string column, string definition)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != System.Data.ConnectionState.Open;
        if (closeAfter) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }
        finally
        {
            if (closeAfter) connection.Close();
        }

        db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

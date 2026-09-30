using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

internal sealed class OnlineMessageSyncRequest
{
    public string InstallationId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string CompanyId { get; init; } = string.Empty;
    public IReadOnlyList<OnlineMessagePayload> Messages { get; init; } = Array.Empty<OnlineMessagePayload>();
}

internal sealed class OnlineMessagePayload
{
    public int LocalMessageId { get; init; }
    public string OnlineMessageId { get; init; } = string.Empty;
    public string CompanyId { get; init; } = string.Empty;
    public int SenderUserId { get; init; }
    public string SenderUsernameSnapshot { get; init; } = string.Empty;
    public string SenderDisplayNameSnapshot { get; init; } = string.Empty;
    public int RecipientUserId { get; init; }
    public string RecipientUsernameSnapshot { get; init; } = string.Empty;
    public string RecipientDisplayNameSnapshot { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Priority { get; init; } = "Normal";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }
    public string? AcknowledgedByUsername { get; init; }
}

internal sealed class OnlineMessageSyncResponse
{
    public bool Ok { get; init; }
    public string? Message { get; init; }
    public List<OnlineCloudMessage> Messages { get; init; } = new();
}

internal sealed class OnlineCloudMessage
{
    public string OnlineMessageId { get; init; } = string.Empty;
    public string CompanyId { get; init; } = string.Empty;
    public int? SourceMessageId { get; init; }
    public string SourceInstallationId { get; init; } = string.Empty;
    public int SenderUserId { get; init; }
    public string SenderUsernameSnapshot { get; init; } = string.Empty;
    public string SenderDisplayNameSnapshot { get; init; } = string.Empty;
    public int RecipientUserId { get; init; }
    public string RecipientUsernameSnapshot { get; init; } = string.Empty;
    public string RecipientDisplayNameSnapshot { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Priority { get; init; } = "Normal";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }
    public string? AcknowledgedByUsername { get; init; }
}

public sealed record OnlineMessageSyncResult(bool Success, string Message, int MessageCount);

public static class OnlineMessageSyncService
{
    private const string Endpoint = "https://europe-west1-solution-compact.cloudfunctions.net/desktopMessagesSync";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public static void QueueSync() => _ = SyncIgnoringErrorsAsync();

    public static async Task<OnlineMessageSyncResult> TrySyncAsync(CancellationToken cancellationToken = default)
    {
        if (!await SyncLock.WaitAsync(0, cancellationToken))
            return new OnlineMessageSyncResult(true, "Nachrichtenabgleich läuft bereits.", 0);

        try
        {
            var settings = LicenseService.EnsureLocalLicenseIdentity();
            if (!string.Equals(settings.LicenseStatus, "active", StringComparison.OrdinalIgnoreCase) ||
                settings.LicenseValidUntilUtc is not { } validUntil || validUntil <= DateTime.UtcNow)
                return new OnlineMessageSyncResult(false, "Nachrichten werden online nur bei aktiver Lizenz synchronisiert.", 0);

            if (string.IsNullOrWhiteSpace(settings.CompanyId))
                return new OnlineMessageSyncResult(false, "Diese Installation ist noch keiner Firma zugeordnet.", 0);

            IReadOnlyList<TenantMessageSyncRow> messages;
            using (var db = new AppDbContext())
            {
                TenantMessagingStore.BindUnassignedUsers(db, settings.CompanyId);
                messages = TenantMessagingStore.LoadForSync(db, settings.CompanyId);
            }

            var request = new OnlineMessageSyncRequest
            {
                InstallationId = settings.LicenseInstallationId,
                Secret = settings.LicenseSecret,
                CompanyId = settings.CompanyId,
                Messages = messages.Select(x => new OnlineMessagePayload
                {
                    LocalMessageId = x.LocalMessageId,
                    OnlineMessageId = x.OnlineMessageId,
                    CompanyId = x.CompanyId,
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
                }).ToList()
            };

            using var response = await Http.PostAsJsonAsync(Endpoint, request, JsonOptions, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            OnlineMessageSyncResponse? payload = null;
            try { payload = JsonSerializer.Deserialize<OnlineMessageSyncResponse>(json, JsonOptions); } catch { }

            if (!response.IsSuccessStatusCode)
                return new OnlineMessageSyncResult(false, payload?.Message ?? ReadError(json) ?? "Nachrichten konnten nicht synchronisiert werden.", 0);

            var cloudMessages = (payload?.Messages ?? new List<OnlineCloudMessage>())
                .Select(x => new TenantCloudMessage(
                    x.OnlineMessageId, x.CompanyId, x.SourceMessageId, x.SourceInstallationId,
                    x.SenderUserId, x.SenderUsernameSnapshot, x.SenderDisplayNameSnapshot,
                    x.RecipientUserId, x.RecipientUsernameSnapshot, x.RecipientDisplayNameSnapshot,
                    x.Subject, x.Body, x.Priority, x.CreatedAtUtc, x.AcknowledgedAtUtc, x.AcknowledgedByUsername))
                .ToList();

            using (var db = new AppDbContext())
            {
                TenantMessagingStore.MergeCloudMessages(db, settings.CompanyId, settings.LicenseInstallationId, cloudMessages);
            }

            return new OnlineMessageSyncResult(true, payload?.Message ?? "Nachrichten synchronisiert.", cloudMessages.Count);
        }
        catch (Exception ex)
        {
            return new OnlineMessageSyncResult(false, "Nachrichten konnten nicht synchronisiert werden. " + ex.Message, 0);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private static async Task SyncIgnoringErrorsAsync()
    {
        try { await TrySyncAsync(); }
        catch { }
    }

    private static string? ReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
        }
        catch { }
        return null;
    }
}

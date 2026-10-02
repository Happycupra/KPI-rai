using System.Net;
using System.Net.Http;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private static void OnlineSyncRetryPersistence()
    {
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        AppSettingsService.Update(settings => settings.PendingOnlineAccessSync = new PendingOnlineAccessSync
        {
            PayloadVersion = "saved-edit", NextRetryAtUtc = now
        });
        OnlineAccessSyncService.RecordResult("saved-edit", new(false, "Offline", 0), now);
        var persisted = AppSettingsService.Load().PendingOnlineAccessSync!;
        Check(persisted.PayloadVersion == "saved-edit" && persisted.Attempts == 1 && persisted.LastError == "Offline",
            "A failed sync must persist the edit intent, attempts and last error");
        Check(persisted.NextRetryAtUtc == now.AddSeconds(30), "First retry must use a 30-second backoff");
        OnlineAccessSyncService.QueueSync(preservePending: true);
        Check(AppSettingsService.Load().PendingOnlineAccessSync!.Attempts == 1,
            "Startup enqueue must preserve retry attempts and backoff");
        for (var i = 0; i < 12; i++)
            OnlineAccessSyncService.RecordResult("saved-edit", new(false, "Offline", 0), now);
        Check(AppSettingsService.Load().PendingOnlineAccessSync!.NextRetryAtUtc == now.AddHours(1),
            "Backoff must cap at one hour");
        // An edit arriving while the old request was in flight must remain queued.
        AppSettingsService.Update(settings => settings.PendingOnlineAccessSync = new PendingOnlineAccessSync { PayloadVersion = "new-edit" });
        OnlineAccessSyncService.RecordResult("saved-edit", new(true, "OK", 1), now);
        Check(AppSettingsService.Load().PendingOnlineAccessSync!.PayloadVersion == "new-edit",
            "A stale acknowledgement must not erase a newer edit");
        OnlineAccessSyncService.RecordResult("new-edit", new(true, "OK", 1), now);
        Check(AppSettingsService.Load().PendingOnlineAccessSync is null, "Successful latest sync must clear the queue");
    }

    private static void OnlineSyncRetryHttpFailure()
    {
        AppSettingsService.Update(settings =>
        {
            settings.CompanyId = "0123456789abcdef0123456789abcdef";
            settings.CompanyCode = "SC-TEST";
            settings.CompanyName = "Test Company";
            settings.LicenseStatus = "active";
            settings.LicenseValidUntilUtc = DateTime.UtcNow.AddDays(1);
            settings.PendingOnlineAccessSync = new PendingOnlineAccessSync { PayloadVersion = "http-edit" };
        });
        var original = OnlineAccessSyncService.Http;
        using var failedHttp = new HttpClient(new PublicationHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"error\":\"Offline\"}")
        })));
        try
        {
            OnlineAccessSyncService.Http = failedHttp;
            Check(!OnlineAccessSyncService.TrySyncAsync().GetAwaiter().GetResult().Success, "HTTP failure must report failure");
            Check(AppSettingsService.Load().PendingOnlineAccessSync?.Attempts == 1, "HTTP failure must persist a retry");
            using var successfulHttp = new HttpClient(new PublicationHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"message\":\"OK\"}")
            })));
            OnlineAccessSyncService.Http = successfulHttp;
            Check(OnlineAccessSyncService.TrySyncAsync().GetAwaiter().GetResult().Success, "Retry must synchronize current users successfully");
            Check(AppSettingsService.Load().PendingOnlineAccessSync is null, "Successful HTTP retry must clear pending intent");
        }
        finally { OnlineAccessSyncService.Http = original; }
    }
}

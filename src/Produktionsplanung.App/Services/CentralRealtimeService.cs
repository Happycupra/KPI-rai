using Microsoft.AspNetCore.SignalR.Client;

namespace Produktionsplanung.App.Services;

public sealed record CentralRealtimeNotice(
    string Type,
    string Id,
    DateTime OccurredAtUtc,
    int SourceUserId,
    IReadOnlyList<string>? EntityTypes);

public static class CentralRealtimeService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HubConnection? connection;

    public static bool IsConnected => connection?.State == HubConnectionState.Connected;

    public static async Task<(bool Success, string Message)> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!CentralModeService.IsEnabled)
            return (true, "Lokaler Betrieb.");
        if (!SessionService.IsAuthenticated || SessionService.CurrentUser is null)
            return (false, "Für die Echtzeitverbindung ist eine Anmeldung erforderlich.");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (connection is { State: HubConnectionState.Connected })
                return (true, "Echtzeitverbindung ist aktiv.");

            var accessToken = await CentralServerClient.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var serverBaseUri = CentralServerClient.GetServerBaseUri();

            if (connection is not null)
            {
                try { await connection.DisposeAsync(); } catch { }
            }

            connection = new HubConnectionBuilder()
                .WithUrl(new Uri(serverBaseUri, "hubs/company"), options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        try { return await CentralServerClient.GetAccessTokenAsync().ConfigureAwait(false); }
                        catch { return accessToken; }
                    };
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            connection.On<CentralRealtimeNotice>("change", notice =>
            {
                try
                {
                    if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (System.Windows.Application.Current.MainWindow is Produktionsplanung.App.MainWindow main)
                                main.ApplyCentralRealtimeChange(notice);
                        }));
                    }
                }
                catch
                {
                    // A realtime refresh must never terminate the desktop client.
                }
            });

            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            return (true, "Zentrale Echtzeitverbindung aktiv.");
        }
        catch (Exception ex)
        {
            return (false, "Echtzeitverbindung konnte nicht hergestellt werden: " + ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void QueueChange(IReadOnlyCollection<string> entityTypes)
    {
        if (!CentralModeService.IsEnabled || entityTypes.Count == 0) return;
        var snapshot = entityTypes.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();
        _ = Task.Run(async () =>
        {
            try
            {
                var hub = connection;
                if (hub?.State != HubConnectionState.Connected) return;
                await hub.InvokeAsync("PublishChange", snapshot).ConfigureAwait(false);
            }
            catch
            {
                // The database transaction is authoritative. Realtime notification is best effort.
            }
        });
    }

    public static async Task StopAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (connection is not null)
            {
                try { await connection.StopAsync().ConfigureAwait(false); } catch { }
                try { await connection.DisposeAsync(); } catch { }
                connection = null;
            }
            CentralServerClient.InvalidateAuthentication();
        }
        finally
        {
            Gate.Release();
        }
    }
}

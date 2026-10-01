using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;

namespace Produktionsplanung.App.Services;

public sealed record CentralRealtimeNotice(
    string Type,
    string Id,
    DateTime OccurredAtUtc,
    int SourceUserId,
    IReadOnlyList<string>? EntityTypes);

internal sealed class CentralDesktopAuthRequest
{
    public string CompanyId { get; init; } = string.Empty;
    public string CompanyCode { get; init; } = string.Empty;
    public string InstallationId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public int SourceUserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
}

internal sealed class CentralDesktopAuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
}

public static class CentralRealtimeService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HubConnection? connection;
    private static string accessToken = string.Empty;
    private static Uri? serverBaseUri;

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

            var central = CentralModeService.RequireEnabledSettings();
            if (!Uri.TryCreate(central.ServerUrl + "/", UriKind.Absolute, out serverBaseUri))
                return (false, "Zentrale Serveradresse ist ungültig.");

            using var http = new HttpClient { BaseAddress = serverBaseUri, Timeout = TimeSpan.FromSeconds(20) };
            var appSettings = AppSettingsService.Load();
            var user = SessionService.CurrentUser;
            var request = new CentralDesktopAuthRequest
            {
                CompanyId = appSettings.CompanyId,
                CompanyCode = appSettings.CompanyCode,
                InstallationId = appSettings.LicenseInstallationId,
                Secret = appSettings.LicenseSecret,
                SourceUserId = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"
            };

            using var response = await http.PostAsJsonAsync("api/v1/auth/desktop", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                return (false, $"Server-Anmeldung fehlgeschlagen ({(int)response.StatusCode}). {detail}".Trim());
            }

            var auth = await response.Content.ReadFromJsonAsync<CentralDesktopAuthResponse>(cancellationToken: cancellationToken);
            if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
                return (false, "Server hat kein gültiges Zugriffstoken geliefert.");
            accessToken = auth.AccessToken;

            if (connection is not null)
            {
                try { await connection.DisposeAsync(); } catch { }
            }

            connection = new HubConnectionBuilder()
                .WithUrl(new Uri(serverBaseUri, "hubs/company"), options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
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

            await connection.StartAsync(cancellationToken);
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
                await hub.InvokeAsync("PublishChange", snapshot);
            }
            catch
            {
                // The database transaction is authoritative. Realtime notification is best effort.
            }
        });
    }

    public static async Task StopAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (connection is not null)
            {
                try { await connection.StopAsync(); } catch { }
                try { await connection.DisposeAsync(); } catch { }
                connection = null;
            }
            accessToken = string.Empty;
        }
        finally
        {
            Gate.Release();
        }
    }
}

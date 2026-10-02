using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class MainWindow
{
    private CancellationTokenSource? centralMessagePollingCts;

    public void EnableCentralMessageMode()
    {
        if (!CentralModeService.IsEnabled || centralMessagePollingCts is not null)
            return;

        messageTimer.Stop();
        centralMessagePollingCts = new CancellationTokenSource();
        Closed += (_, _) =>
        {
            try { centralMessagePollingCts?.Cancel(); } catch { }
            centralMessagePollingCts?.Dispose();
            centralMessagePollingCts = null;
            UserMessageService.ResetCentralUnreadCache();
        };

        _ = RefreshCentralMessagesAsync(showPopup: true, centralMessagePollingCts.Token);
        _ = RunCentralMessagePollingAsync(centralMessagePollingCts.Token);
    }

    public async void ApplyCentralRealtimeChange(CentralRealtimeNotice notice)
    {
        if (!CentralModeService.IsEnabled)
            return;

        if (notice.Type.StartsWith("message.", StringComparison.OrdinalIgnoreCase))
        {
            var showPopup = string.Equals(notice.Type, "message.received", StringComparison.OrdinalIgnoreCase);
            await RefreshCentralMessagesAsync(showPopup, centralMessagePollingCts?.Token ?? CancellationToken.None);
            if (messageCenterWindow is not null)
                await messageCenterWindow.RefreshFromServerAsync();
            return;
        }

        if (notice.SourceUserId == SessionService.CurrentUser?.Id)
            return;

        RefreshNotifications();
        await RefreshCentralMessagesAsync(showPopup: false, centralMessagePollingCts?.Token ?? CancellationToken.None);

        if (ContentHost.Content is IUnsavedChangesAware dirtyAware && dirtyAware.HasUnsavedChanges)
        {
            Title = BaseWindowTitle + " · Neue Serverdaten verfügbar";
            return;
        }

        if (currentNavigation is not null)
            Navigate(currentNavigation, addToHistory: false);
    }

    private async Task RunCentralMessagePollingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                if (licenseBlocked || sessionLocked || !SessionService.IsAuthenticated || messagePopupOpen)
                    continue;
                await RefreshCentralMessagesAsync(showPopup: true, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RefreshCentralMessagesAsync(bool showPopup, CancellationToken cancellationToken)
    {
        if (!CentralModeService.IsEnabled || !SessionService.IsAuthenticated)
            return;

        try
        {
            var unread = await UserMessageService.GetUnreadAsync(cancellationToken).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateCentralMessageBadge(unread.Count);
                if (showPopup && unread.Count > 0)
                    ShowNextCentralMessagePopup(unread);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageCountBadge.Visibility = Visibility.Collapsed;
                MessageButton.ToolTip = "Persönliche Hinweise konnten nicht vom Server geladen werden";
            });
        }
    }

    private void UpdateCentralMessageBadge(int count)
    {
        MessageCountText.Text = count > 99 ? "99+" : count.ToString();
        MessageCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageButton.ToolTip = count == 0
            ? "Persönliche Hinweise · keine ungelesenen Nachrichten"
            : $"Persönliche Hinweise · {count} ungelesen";
    }

    private async void ShowNextCentralMessagePopup(IReadOnlyList<UserMessageRow> unread)
    {
        if (messagePopupOpen || sessionLocked || !SessionService.IsAuthenticated)
            return;

        var message = unread.FirstOrDefault(x => !deferredMessageIds.Contains(x.Id));
        if (message is null)
            return;

        messagePopupOpen = true;
        try
        {
            var popup = new MessagePopupWindow(message) { Owner = this };
            if (popup.ShowDialog() == true)
            {
                await UserMessageService.AcknowledgeAsync(message.Id);
                deferredMessageIds.Remove(message.Id);
                await RefreshCentralMessagesAsync(showPopup: false, centralMessagePollingCts?.Token ?? CancellationToken.None);
                _ = Dispatcher.BeginInvoke(new Action(() => ShowNextCentralMessagePopup(UserMessageService.GetUnread())));
            }
            else
            {
                deferredMessageIds.Add(message.Id);
                await RefreshCentralMessagesAsync(showPopup: false, centralMessagePollingCts?.Token ?? CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Der persönliche Hinweis konnte nicht verarbeitet werden.\n\n" + ex.Message,
                "SolutionCompakt – Hinweise",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            messagePopupOpen = false;
        }
    }
}

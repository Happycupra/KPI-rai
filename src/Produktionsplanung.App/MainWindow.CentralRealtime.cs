using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class MainWindow
{
    public void ApplyCentralRealtimeChange(CentralRealtimeNotice notice)
    {
        if (!CentralModeService.IsEnabled)
            return;

        if (notice.Type.StartsWith("message.", StringComparison.OrdinalIgnoreCase))
        {
            var showPopup = string.Equals(notice.Type, "message.received", StringComparison.OrdinalIgnoreCase);
            RefreshPersonalMessages(showPopup);
            messageCenterWindow?.RefreshFromServer();
            return;
        }

        if (notice.SourceUserId == SessionService.CurrentUser?.Id)
            return;

        RefreshNotifications();
        RefreshPersonalMessages(showPopup: true);

        if (ContentHost.Content is IUnsavedChangesAware dirtyAware && dirtyAware.HasUnsavedChanges)
        {
            Title = BaseWindowTitle + " · Neue Serverdaten verfügbar";
            return;
        }

        if (currentNavigation is not null)
            Navigate(currentNavigation, addToHistory: false);
    }
}

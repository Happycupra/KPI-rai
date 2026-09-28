using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class PublishWeekPlanWindow : Window
{
    private readonly OnlineWeekPlanSnapshot snapshot;
    private bool publishing;
    public OnlineWeekPlanPublication? Publication { get; private set; }
    public string ReceiptWarning { get; private set; } = string.Empty;

    public PublishWeekPlanWindow(OnlineWeekPlanSnapshot snapshot)
    {
        InitializeComponent();
        this.snapshot = snapshot;
        SummaryText.Text = $"KW {snapshot.IsoWeek:00} · {snapshot.WeekStart} bis {snapshot.WeekEnd}\n{snapshot.Entries.Count} Personaleinsätze · {snapshot.ProductionSlots.Count} Produktionsschichten";
        UsernameText.Text = "Administrator: " + SessionService.CurrentUser?.Username;
        Loaded += (_, _) => PasswordInput.Focus();
        Closing += (_, e) => { if (publishing) e.Cancel = true; else PasswordInput.Clear(); };
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (publishing) return;
        if (string.IsNullOrEmpty(PasswordInput.Password))
        {
            StatusText.Text = "Bitte dein App-Passwort eingeben.";
            PasswordInput.Focus();
            return;
        }
        publishing = true;
        PublishButton.IsEnabled = CancelButton.IsEnabled = PasswordInput.IsEnabled = false;
        StatusText.Text = "Anmeldung und Veröffentlichung laufen …";
        try
        {
            Publication = await OnlineWeekPlanPublisher.PublishAsync(snapshot, PasswordInput.Password);
        }
        catch (PublicationReceiptException ex)
        {
            Publication = ex.Publication;
            ReceiptWarning = ex.Message;
        }
        catch (System.Net.Http.HttpRequestException)
        {
            StatusText.Text = "Keine Serververbindung. Der Veröffentlichungsstatus ist unbestätigt. Bitte den Online-Plan prüfen und erneut versuchen.";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "Zeitüberschreitung. Bitte den Online-Plan prüfen, bevor du erneut veröffentlichst.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex is System.Text.Json.JsonException
                ? "Ungültige Serverantwort. Bitte den Online-Plan prüfen." : ex.Message;
        }
        finally
        {
            publishing = false;
            PublishButton.IsEnabled = CancelButton.IsEnabled = PasswordInput.IsEnabled = true;
            PasswordInput.Clear();
        }
        if (Publication is not null) DialogResult = true;
    }
}

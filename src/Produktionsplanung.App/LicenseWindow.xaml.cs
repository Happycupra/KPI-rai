using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class LicenseWindow : Window
{
    public LicenseWindow(string? initialMessage = null)
    {
        InitializeComponent();
        var settings = AppSettingsService.Load();
        CompanyNameBox.Text = string.IsNullOrWhiteSpace(settings.CompanyName) ||
                              string.Equals(settings.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : settings.CompanyName;
        EmailBox.Text = settings.LicenseEmail;
        CurrentStatusBlock.Text = BuildCurrentStatus(settings);
        StatusBlock.Text = initialMessage ?? string.Empty;
    }

    private async void Request_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var result = await LicenseService.RequestRegistrationAsync(CompanyNameBox.Text, EmailBox.Text);
            StatusBlock.Text = result.Message;
            CurrentStatusBlock.Text = BuildCurrentStatus(AppSettingsService.Load());
            if (result.Success)
                CheckButton.Focus();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var result = await LicenseService.CheckOnlineAsync();
            StatusBlock.Text = result.Message;
            CurrentStatusBlock.Text = BuildCurrentStatus(AppSettingsService.Load());

            if (result.Success &&
                string.Equals(result.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                result.ValidUntilUtc is { } validUntil &&
                validUntil > DateTime.UtcNow)
            {
                MessageBox.Show(
                    this,
                    $"SolutionCompakt ist bis {validUntil.ToLocalTime():d} freigeschaltet.",
                    "Freischaltung bestätigt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetBusy(bool busy)
    {
        RequestButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy;
    }

    private static string BuildCurrentStatus(AppSettings settings)
    {
        var status = string.IsNullOrWhiteSpace(settings.LicenseStatus) ? "noch nicht registriert" : settings.LicenseStatus;
        var valid = settings.LicenseValidUntilUtc.HasValue
            ? $" · gültig bis {settings.LicenseValidUntilUtc.Value.ToLocalTime():d}"
            : string.Empty;
        return $"Lizenzstatus: {status}{valid}";
    }
}

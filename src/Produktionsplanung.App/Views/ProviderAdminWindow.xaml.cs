using System.Windows;
using System.Windows.Input;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class ProviderAdminWindow : Window
{
    public ProviderAdminWindow()
    {
        InitializeComponent();
        OwnerEmail.Text = ProviderAdminService.OwnerEmail;
        Loaded += ProviderAdminWindow_Loaded;
    }

    private async void ProviderAdminWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!ProviderAdminService.IsSignedIn)
        {
            ShowLogin();
            OwnerPassword.Focus();
            return;
        }

        ShowAdmin();
        await LoadLicensesAsync();
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await LoginAsync();

    private async void OwnerPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoginAsync();
        }
    }

    private async Task LoginAsync()
    {
        LoginButton.IsEnabled = false;
        LoginStatus.Text = "Anmeldung wird geprüft…";
        try
        {
            await ProviderAdminService.SignInAsync(OwnerPassword.Password);
            OwnerPassword.Clear();
            LoginStatus.Text = string.Empty;
            ShowAdmin();
            await LoadLicensesAsync();
        }
        catch (Exception ex)
        {
            LoginStatus.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowLogin()
    {
        AdminPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
    }

    private void ShowAdmin()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Visible;
    }

    private async Task LoadLicensesAsync(string? successMessage = null)
    {
        SetAdminBusy(true, "Firmen und Lizenzen werden geladen…");
        try
        {
            var selectedId = SelectedLicense?.InstallationId;
            var licenses = await ProviderAdminService.LoadLicensesAsync();
            LicenseGrid.ItemsSource = licenses;
            CountLabel.Text = $"{licenses.Count} Registrierung(en)";
            LicenseGrid.SelectedItem = licenses.FirstOrDefault(x => x.InstallationId == selectedId) ?? licenses.FirstOrDefault();
            AdminStatus.Text = successMessage ?? (licenses.Count == 0 ? "Noch keine Registrierungen vorhanden." : string.Empty);
        }
        catch (Exception ex)
        {
            AdminStatus.Text = ex.Message;
            if (!ProviderAdminService.IsSignedIn)
                ShowLogin();
        }
        finally
        {
            SetAdminBusy(false);
        }
    }

    private ProviderLicenseRecord? SelectedLicense => LicenseGrid.SelectedItem as ProviderLicenseRecord;

    private async void Reload_Click(object sender, RoutedEventArgs e) => await LoadLicensesAsync();

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        ProviderAdminService.SignOut();
        LicenseGrid.ItemsSource = null;
        OwnerPassword.Clear();
        LoginStatus.Text = string.Empty;
        ShowLogin();
        OwnerPassword.Focus();
    }

    private async void Extend_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string raw } || !int.TryParse(raw, out var days))
            return;
        await ExtendSelectedAsync(days);
    }

    private async void CustomExtend_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CustomDays.Text, out var days) || days is < 1 or > 3650)
        {
            AdminStatus.Text = "Bitte 1 bis 3650 Tage eingeben.";
            return;
        }
        await ExtendSelectedAsync(days);
    }

    private async Task ExtendSelectedAsync(int days)
    {
        var item = SelectedLicense;
        if (item is null)
        {
            AdminStatus.Text = "Bitte zuerst eine Firma auswählen.";
            return;
        }

        SetAdminBusy(true, $"Lizenz für {item.CompanyName} wird um {days} Tage verlängert…");
        try
        {
            await ProviderAdminService.ExtendLicenseAsync(item.InstallationId, days);
            CustomDays.Clear();
            await LoadLicensesAsync($"Lizenz für {item.CompanyName} wurde um {days} Tage verlängert.");
        }
        catch (Exception ex)
        {
            AdminStatus.Text = ex.Message;
            SetAdminBusy(false);
        }
    }

    private async void Suspend_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedLicense;
        if (item is null)
        {
            AdminStatus.Text = "Bitte zuerst eine Firma auswählen.";
            return;
        }

        if (MessageBox.Show(this,
                $"Lizenz von „{item.CompanyName}“ wirklich sperren?",
                "Lizenz sperren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetAdminBusy(true, $"Lizenz für {item.CompanyName} wird gesperrt…");
        try
        {
            await ProviderAdminService.SuspendLicenseAsync(item.InstallationId);
            await LoadLicensesAsync($"Lizenz für {item.CompanyName} wurde gesperrt.");
        }
        catch (Exception ex)
        {
            AdminStatus.Text = ex.Message;
            SetAdminBusy(false);
        }
    }

    private async void Recovery_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedLicense;
        if (item is null)
        {
            AdminStatus.Text = "Bitte zuerst eine Firma auswählen.";
            return;
        }
        if (!item.HasRecoveryCode)
        {
            AdminStatus.Text = "Für diese Installation ist noch kein Recovery-Code hinterlegt.";
            return;
        }

        SetAdminBusy(true, "Recovery-Code wird geladen…");
        try
        {
            var code = await ProviderAdminService.GetRecoveryCodeAsync(item.InstallationId);
            Clipboard.SetText(code);
            MessageBox.Show(this,
                $"Recovery-Code für {item.CompanyName}:\n\n{code}\n\nDer Code wurde in die Zwischenablage kopiert.",
                "Recovery-Code",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            AdminStatus.Text = "Recovery-Code wurde angezeigt und kopiert.";
        }
        catch (Exception ex)
        {
            AdminStatus.Text = ex.Message;
        }
        finally
        {
            SetAdminBusy(false);
        }
    }

    private void SetAdminBusy(bool busy, string? message = null)
    {
        AdminPanel.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
            AdminStatus.Text = message;
    }
}

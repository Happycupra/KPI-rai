using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class RecoveryCodeManagementWindow : Window
{
    public RecoveryCodeManagementWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        StatusBlock.Text = string.Empty;

        if (!SessionService.IsAdministrator)
        {
            StatusBlock.Text = "Nur ein Administrator darf den Recovery-Code erneuern.";
            return;
        }

        var verified = AuthenticationService.VerifyCurrentPassword(CurrentPasswordBox.Password);
        if (!verified.Success)
        {
            StatusBlock.Text = verified.Message;
            CurrentPasswordBox.SelectAll();
            CurrentPasswordBox.Focus();
            return;
        }

        if (MessageBox.Show(
                "Der bisherige Recovery-Code wird sofort ungültig. Wirklich einen neuen Code erzeugen?",
                "Recovery-Code erneuern",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var code = RecoveryCodeService.CreateOrReplaceRecoveryCode();
            RecoveryCodeBox.Text = code;
            CurrentPasswordBox.Clear();
            VerificationPanel.Visibility = Visibility.Collapsed;
            GeneratedPanel.Visibility = Visibility.Visible;
            GenerateButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            DoneButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusBlock.Text = $"Recovery-Code konnte nicht erneuert werden: {ex.Message}";
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecoveryCodeBox.Text))
            return;

        Clipboard.SetText(RecoveryCodeBox.Text);
        CopyStatusBlock.Text = "Recovery-Code wurde in die Zwischenablage kopiert.";
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

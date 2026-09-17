using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class PasswordRecoveryWindow : Window
{
    public PasswordRecoveryWindow(string? username = null)
    {
        InitializeComponent();
        UsernameBox.Text = username?.Trim() ?? string.Empty;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(UsernameBox.Text))
                UsernameBox.Focus();
            else
                RecoveryCodeBox.Focus();
        };
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        StatusBlock.Text = string.Empty;
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            StatusBlock.Text = "Die Passwörter stimmen nicht überein.";
            return;
        }

        var result = RecoveryCodeService.ResetPassword(
            UsernameBox.Text,
            RecoveryCodeBox.Text,
            NewPasswordBox.Password);

        StatusBlock.Text = result.Message;
        StatusBlock.Foreground = result.Success
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));

        if (!result.Success)
            return;

        MessageBox.Show(result.Message, "Passwort zurückgesetzt", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

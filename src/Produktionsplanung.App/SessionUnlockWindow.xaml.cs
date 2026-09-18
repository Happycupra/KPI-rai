using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class SessionUnlockWindow : Window
{
    private bool unlocked;

    public SessionUnlockWindow()
    {
        InitializeComponent();
        var user = SessionService.CurrentUser;
        UserBlock.Text = user is null
            ? "Keine aktive Sitzung"
            : $"{user.DisplayName} · {user.Username}";
        Loaded += (_, _) => PasswordBox.Focus();
        Closing += SessionUnlockWindow_Closing;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryUnlock();
        }
    }

    private void TryUnlock()
    {
        StatusBlock.Text = string.Empty;
        var verification = AuthenticationService.VerifyCurrentPassword(PasswordBox.Password);
        if (!verification.Success)
        {
            StatusBlock.Text = verification.Message;
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        unlocked = true;
        DialogResult = true;
        Close();
    }

    private void SessionUnlockWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!unlocked)
            e.Cancel = true;
    }
}

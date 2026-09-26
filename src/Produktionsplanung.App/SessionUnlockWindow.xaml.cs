using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class SessionUnlockWindow : Window
{
    private bool unlocked;
    private bool usePin;
    private int failedPinAttempts;

    public SessionUnlockWindow()
    {
        InitializeComponent();
        var user = SessionService.CurrentUser;
        UserBlock.Text = user is null
            ? "Keine aktive Sitzung"
            : $"{user.DisplayName} · {user.Username}";
        usePin = QuickAccessService.CanUseForCurrentUser();
        ApplyCredentialMode();
        Loaded += (_, _) => PasswordBox.Focus();
        Closing += SessionUnlockWindow_Closing;
    }

    private void ApplyCredentialMode()
    {
        CredentialLabel.Text = usePin ? "4-stelliger Zugangs-PIN" : "Passwort";
        ModeHint.Text = usePin
            ? "Die Oberfläche wurde gesperrt. Zum Fortfahren genügt dein 4-stelliger Zugangs-PIN."
            : "Die Oberfläche wurde wegen Inaktivität gesperrt. Offene Eingaben bleiben erhalten.";
        PasswordBox.MaxLength = usePin ? 4 : 0;
        PasswordFallbackButton.Visibility = usePin ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Clear();
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

    private void PasswordFallback_Click(object sender, RoutedEventArgs e)
    {
        usePin = false;
        failedPinAttempts = 0;
        StatusBlock.Text = string.Empty;
        ApplyCredentialMode();
        PasswordBox.Focus();
    }

    private void TryUnlock()
    {
        StatusBlock.Text = string.Empty;
        if (usePin)
        {
            if (!QuickAccessService.VerifyCurrentPin(PasswordBox.Password))
            {
                failedPinAttempts++;
                StatusBlock.Text = "Der Zugangs-PIN ist nicht korrekt.";
                PasswordBox.Clear();
                if (failedPinAttempts >= 5)
                {
                    usePin = false;
                    ApplyCredentialMode();
                    StatusBlock.Text = "Zu viele falsche PIN-Versuche. Bitte mit dem Passwort entsperren.";
                }
                PasswordBox.Focus();
                return;
            }
        }
        else
        {
            var verification = AuthenticationService.VerifyCurrentPassword(PasswordBox.Password);
            if (!verification.Success)
            {
                StatusBlock.Text = verification.Message;
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }
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

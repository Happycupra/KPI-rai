using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class LoginWindow : Window
{
    private readonly bool _setupMode;

    public LoginWindow()
    {
        InitializeComponent();
        _setupMode = !AuthenticationService.HasUsers();

        if (_setupMode)
        {
            Title = "KPI-rai Ersteinrichtung";
            ModeTitle.Text = "Ersteinrichtung";
            ModeDescription.Text = "Lege den ersten lokalen Administrator an. Es gibt kein voreingestelltes Standardpasswort.";
            DisplayNamePanel.Visibility = Visibility.Visible;
            ConfirmPasswordPanel.Visibility = Visibility.Visible;
            SubmitButton.Content = "Administrator anlegen und anmelden";
            UsernameBox.Text = "admin";
        }

        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        StatusBlock.Text = string.Empty;
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (_setupMode)
        {
            if (password != ConfirmPasswordBox.Password)
            {
                StatusBlock.Text = "Die Passwörter stimmen nicht überein.";
                return;
            }

            var created = AuthenticationService.CreateInitialAdministrator(username, DisplayNameBox.Text, password);
            if (!created.Success)
            {
                StatusBlock.Text = created.Message;
                return;
            }
        }

        var login = AuthenticationService.Login(username, password);
        if (!login.Success)
        {
            StatusBlock.Text = login.Message;
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

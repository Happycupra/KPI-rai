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
            Title = "SolutionCompakt Ersteinrichtung";
            ModeTitle.Text = "Firmenregistrierung & Ersteinrichtung";
            ModeDescription.Text = "Registriere diese Installation für eine Firma und lege den ersten lokalen Administrator an.";
            CompanyPanel.Visibility = Visibility.Visible;
            DisplayNamePanel.Visibility = Visibility.Visible;
            ConfirmPasswordPanel.Visibility = Visibility.Visible;
            ForgotPasswordButton.Visibility = Visibility.Collapsed;
            SubmitButton.Content = "Administrator anlegen und anmelden";
            UsernameBox.Text = "admin";
        }
        else
        {
            var company = AppSettingsService.Load();
            if (!string.IsNullOrWhiteSpace(company.CompanyName) &&
                !string.Equals(company.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase))
            {
                ModeDescription.Text = $"{company.CompanyName} · {company.CompanyCode}\nMit deinem lokalen Benutzerkonto anmelden.";
            }
        }

        Loaded += (_, _) => (_setupMode ? CompanyNameBox : UsernameBox).Focus();
    }

    private void CompanyNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_setupMode || CompanyCodeBox is null || !string.IsNullOrWhiteSpace(CompanyCodeBox.Text))
            return;

        CompanyCodeBox.Text = CompanyIdentityService.SuggestCode(CompanyNameBox.Text);
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        StatusBlock.Text = string.Empty;
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        string? newlyCreatedRecoveryCode = null;

        if (_setupMode)
        {
            if (password != ConfirmPasswordBox.Password)
            {
                StatusBlock.Text = "Die Passwörter stimmen nicht überein.";
                return;
            }

            var created = AuthenticationService.CreateInitialAdministrator(
                CompanyNameBox.Text,
                CompanyCodeBox.Text,
                username,
                DisplayNameBox.Text,
                password);
            if (!created.Success)
            {
                StatusBlock.Text = created.Message;
                return;
            }

            newlyCreatedRecoveryCode = RecoveryCodeService.CreateOrReplaceRecoveryCode(
                allowWithoutAuthenticatedAdministrator: true);
        }

        var login = AuthenticationService.Login(username, password);
        if (!login.Success)
        {
            StatusBlock.Text = login.Message;
            return;
        }

        if (login.User?.Role == Models.UserRoles.Administrator && !RecoveryCodeService.HasRecoveryCode())
            newlyCreatedRecoveryCode = RecoveryCodeService.CreateOrReplaceRecoveryCode();

        if (!string.IsNullOrWhiteSpace(newlyCreatedRecoveryCode))
            ShowRecoveryCode(newlyCreatedRecoveryCode);

        DialogResult = true;
        Close();
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordRecoveryWindow(UsernameBox.Text) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
            StatusBlock.Text = "Passwort zurückgesetzt. Bitte mit dem neuen Passwort anmelden.";
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private static void ShowRecoveryCode(string code)
    {
        MessageBox.Show(
            "WICHTIG: Bewahre diesen Recovery-Code sicher ausserhalb der App auf.\n\n" +
            $"{code}\n\n" +
            "Mit diesem Code kann am Anmeldefenster ein vergessenes Passwort zurückgesetzt werden. " +
            "Der Code wird aus Sicherheitsgründen nicht im Klartext gespeichert und kann später nicht angezeigt werden.",
            "SolutionCompakt Recovery-Code",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

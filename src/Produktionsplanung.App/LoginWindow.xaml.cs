using System.Windows;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class LoginWindow : Window
{
    private readonly bool _setupMode;
    private bool _quickAccessMode;
    private bool _updatingCompanyCode;
    private bool _companyCodeTouched;
    private int _failedPinAttempts;

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
            var remembered = QuickAccessService.GetRememberedUser();
            if (remembered is not null)
            {
                _quickAccessMode = true;
                StandardLoginPanel.Visibility = Visibility.Collapsed;
                QuickAccessPanel.Visibility = Visibility.Visible;
                ModeTitle.Text = "Willkommen zurück";
                ModeDescription.Text = "Diese Anmeldung bleibt auf diesem Gerät gespeichert und ist mit deinem 4-stelligen PIN geschützt.";
                QuickAccessUserBlock.Text = $"{remembered.DisplayName} · {remembered.Username}";
                UsernameBox.Text = remembered.Username;
                RememberMeCheckBox.IsChecked = true;
                SubmitButton.Content = "Mit PIN anmelden";
            }
            else
            {
                ApplyCompanyDescription();
            }
        }

        Loaded += (_, _) =>
        {
            if (_quickAccessMode)
                QuickPinBox.Focus();
            else if (_setupMode)
                CompanyNameBox.Focus();
            else
                UsernameBox.Focus();
        };
    }

    private void ApplyCompanyDescription()
    {
        var company = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(company.CompanyName) &&
            !string.Equals(company.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase))
        {
            ModeDescription.Text = $"{company.CompanyName} · {company.CompanyCode}\nMit deinem lokalen Benutzerkonto anmelden.";
        }
        else
        {
            ModeDescription.Text = "Mit deinem lokalen Benutzerkonto anmelden.";
        }
    }

    private void CompanyNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_setupMode || CompanyCodeBox is null || _companyCodeTouched)
            return;
        _updatingCompanyCode = true;
        CompanyCodeBox.Text = CompanyIdentityService.SuggestCode(CompanyNameBox.Text);
        CompanyCodeBox.CaretIndex = CompanyCodeBox.Text.Length;
        _updatingCompanyCode = false;
    }

    private void CompanyCodeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_setupMode && !_updatingCompanyCode)
            _companyCodeTouched = true;
    }

    private void RememberMe_Changed(object sender, RoutedEventArgs e)
    {
        if (PinSetupPanel is null)
            return;
        PinSetupPanel.Visibility = RememberMeCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (RememberMeCheckBox.IsChecked != true)
        {
            AccessPinBox.Clear();
            ConfirmAccessPinBox.Clear();
        }
    }

    private void SwitchToPassword_Click(object sender, RoutedEventArgs e)
    {
        _quickAccessMode = false;
        _failedPinAttempts = 0;
        QuickAccessPanel.Visibility = Visibility.Collapsed;
        StandardLoginPanel.Visibility = Visibility.Visible;
        RememberMeCheckBox.IsChecked = true;
        PinSetupPanel.Visibility = Visibility.Visible;
        ModeTitle.Text = "Anmeldung mit Passwort";
        ApplyCompanyDescription();
        SubmitButton.Content = "Anmelden";
        StatusBlock.Text = string.Empty;
        PasswordBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        StatusBlock.Text = string.Empty;

        if (_quickAccessMode)
        {
            var quick = QuickAccessService.LoginWithPin(QuickPinBox.Password);
            QuickPinBox.Clear();
            if (!quick.Success)
            {
                _failedPinAttempts++;
                StatusBlock.Text = quick.Message;
                if (_failedPinAttempts >= 5)
                {
                    SwitchToPassword_Click(sender, e);
                    StatusBlock.Text = "Zu viele falsche PIN-Versuche. Bitte einmal mit dem Passwort anmelden.";
                }
                else
                {
                    QuickPinBox.Focus();
                }
                return;
            }

            OnlineAccessSyncService.QueueSync();
            DialogResult = true;
            Close();
            return;
        }

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        string? newlyCreatedRecoveryCode = null;

        if (RememberMeCheckBox.IsChecked == true)
        {
            var pinValidation = QuickAccessService.ValidatePinPair(AccessPinBox.Password, ConfirmAccessPinBox.Password);
            if (pinValidation is not null)
            {
                StatusBlock.Text = pinValidation;
                return;
            }
        }

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
        if (!login.Success || login.User is null)
        {
            StatusBlock.Text = login.Message;
            return;
        }

        if (RememberMeCheckBox.IsChecked == true)
        {
            var configured = QuickAccessService.Configure(login.User, AccessPinBox.Password, ConfirmAccessPinBox.Password);
            if (!configured.Success)
            {
                StatusBlock.Text = configured.Message;
                SessionService.SignOut();
                return;
            }
        }
        else
        {
            QuickAccessService.Clear();
        }

        if (login.User.Role == UserRoles.Administrator && !RecoveryCodeService.HasRecoveryCode())
            newlyCreatedRecoveryCode = RecoveryCodeService.CreateOrReplaceRecoveryCode();

        if (!string.IsNullOrWhiteSpace(newlyCreatedRecoveryCode))
            ShowRecoveryCode(newlyCreatedRecoveryCode);

        OnlineAccessSyncService.QueueSync();
        DialogResult = true;
        Close();
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordRecoveryWindow(UsernameBox.Text) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            StatusBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
            StatusBlock.Text = "Passwort zurückgesetzt. Ein gespeicherter PIN-Zugang wurde aus Sicherheitsgründen ebenfalls aufgehoben.";
            PasswordBox.Clear();
            RememberMeCheckBox.IsChecked = false;
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

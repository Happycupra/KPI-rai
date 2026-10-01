using System.Windows;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App;

public partial class LoginWindow : Window
{
    private readonly bool _hasUsers;
    private bool _registrationMode;
    private bool _quickAccessMode;
    private int _failedPinAttempts;

    public LoginWindow()
    {
        InitializeComponent();
        _hasUsers = AuthenticationService.HasUsers();
        ModeSwitchContainer.Visibility = _hasUsers ? Visibility.Collapsed : Visibility.Visible;

        if (_hasUsers)
            ApplyLoginMode(preferQuickAccess: true);
        else
            ApplyRegistrationMode();

        Loaded += (_, _) =>
        {
            if (_quickAccessMode)
                QuickPinBox.Focus();
            else if (_registrationMode)
                CompanyNameBox.Focus();
            else
                UsernameBox.Focus();
        };
    }

    private void LoginMode_Click(object sender, RoutedEventArgs e)
    {
        ApplyLoginMode(preferQuickAccess: true);
    }

    private void RegisterMode_Click(object sender, RoutedEventArgs e)
    {
        ApplyRegistrationMode();
    }

    private void ApplyLoginMode(bool preferQuickAccess)
    {
        _registrationMode = false;
        _failedPinAttempts = 0;
        Title = "SolutionCompakt Anmeldung";
        CompanyPanel.Visibility = Visibility.Collapsed;
        ConfirmPasswordPanel.Visibility = Visibility.Collapsed;
        ForgotPasswordButton.Visibility = Visibility.Visible;
        ConfirmPasswordBox.Clear();
        UpdateModeButtonStyles();

        var remembered = preferQuickAccess && _hasUsers
            ? QuickAccessService.GetRememberedUser()
            : null;

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
            SetStatus(string.Empty);
            QuickPinBox.Focus();
            return;
        }

        _quickAccessMode = false;
        QuickAccessPanel.Visibility = Visibility.Collapsed;
        StandardLoginPanel.Visibility = Visibility.Visible;
        ModeTitle.Text = "Anmeldung";
        ApplyCompanyDescription();
        SubmitButton.Content = "Anmelden";

        if (!_hasUsers)
            SetStatus("Auf dieser Installation ist noch kein Benutzerkonto vorhanden. Du kannst dich mit dem Anbieter-Konto anmelden oder die Firma zuerst registrieren.");
        else
            SetStatus(string.Empty);
    }

    private void ApplyRegistrationMode()
    {
        if (_hasUsers)
        {
            ApplyLoginMode(preferQuickAccess: true);
            return;
        }

        _registrationMode = true;
        _quickAccessMode = false;
        _failedPinAttempts = 0;
        Title = "SolutionCompakt Registrierung";
        QuickAccessPanel.Visibility = Visibility.Collapsed;
        StandardLoginPanel.Visibility = Visibility.Visible;
        CompanyPanel.Visibility = Visibility.Visible;
        ConfirmPasswordPanel.Visibility = Visibility.Visible;
        ForgotPasswordButton.Visibility = Visibility.Collapsed;
        ModeTitle.Text = "Firma registrieren";
        ModeDescription.Text = "Firmenname und erstes Administratorkonto einrichten.";
        SubmitButton.Content = "Firma registrieren und anmelden";

        var current = AppSettingsService.Load();
        if (CompanyIdentityService.IsRegistered())
        {
            CompanyNameBox.Text = current.CompanyName;
            ModeDescription.Text = "Die Firma wurde bereits gespeichert. Schließe die Ersteinrichtung mit dem Administratorkonto ab.";
        }

        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
            UsernameBox.Text = "admin";

        SetStatus(string.Empty);
        UpdateModeButtonStyles();
        CompanyNameBox.Focus();
    }

    private void UpdateModeButtonStyles()
    {
        if (LoginModeButton is null || RegisterModeButton is null)
            return;

        LoginModeButton.Style = (Style)FindResource(_registrationMode
            ? "ActionButtonStyle"
            : "PrimaryActionButtonStyle");
        RegisterModeButton.Style = (Style)FindResource(_registrationMode
            ? "PrimaryActionButtonStyle"
            : "ActionButtonStyle");
    }

    private void SetStatus(string message, bool success = false)
    {
        StatusBlock.Foreground = (System.Windows.Media.Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        StatusBlock.Text = message;
    }

    private void ApplyCompanyDescription()
    {
        var company = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(company.CompanyName) &&
            !string.Equals(company.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase))
        {
            ModeDescription.Text = $"{company.CompanyName}\nMit deinem lokalen Benutzerkonto anmelden.";
        }
        else
        {
            ModeDescription.Text = "Mit deinem lokalen Benutzerkonto anmelden.";
        }
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
        ApplyLoginMode(preferQuickAccess: false);
        RememberMeCheckBox.IsChecked = true;
        PinSetupPanel.Visibility = Visibility.Visible;
        ModeTitle.Text = "Anmeldung mit Passwort";
        ApplyCompanyDescription();
        SubmitButton.Content = "Anmelden";
        PasswordBox.Focus();
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(string.Empty);

        if (_quickAccessMode)
        {
            var quick = QuickAccessService.LoginWithPin(QuickPinBox.Password);
            QuickPinBox.Clear();
            if (!quick.Success)
            {
                _failedPinAttempts++;
                SetStatus(quick.Message);
                if (_failedPinAttempts >= 5)
                {
                    SwitchToPassword_Click(sender, e);
                    SetStatus("Zu viele falsche PIN-Versuche. Bitte einmal mit dem Passwort anmelden.");
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

        if (!_registrationMode && string.Equals(username, ProviderAdminService.OwnerEmail, StringComparison.OrdinalIgnoreCase))
        {
            await OpenProviderAdministrationAsync(password);
            return;
        }

        if (!_registrationMode && !_hasUsers)
        {
            SetStatus("Auf dieser Installation ist noch kein Benutzerkonto vorhanden. Bitte zuerst „Registrieren“ wählen.");
            return;
        }

        string? newlyCreatedRecoveryCode = null;

        if (RememberMeCheckBox.IsChecked == true)
        {
            var pinValidation = QuickAccessService.ValidatePinPair(AccessPinBox.Password, ConfirmAccessPinBox.Password);
            if (pinValidation is not null)
            {
                SetStatus(pinValidation);
                return;
            }
        }

        if (_registrationMode)
        {
            if (password != ConfirmPasswordBox.Password)
            {
                SetStatus("Die Passwörter stimmen nicht überein.");
                return;
            }

            var current = AppSettingsService.Load();
            var companyCode = CompanyIdentityService.IsRegistered()
                ? current.CompanyCode
                : CompanyIdentityService.CreateRegistrationCode(CompanyNameBox.Text);

            var created = AuthenticationService.CreateInitialAdministrator(
                CompanyNameBox.Text,
                companyCode,
                username,
                username,
                password);
            if (!created.Success)
            {
                SetStatus(created.Message);
                return;
            }

            newlyCreatedRecoveryCode = RecoveryCodeService.CreateOrReplaceRecoveryCode(
                allowWithoutAuthenticatedAdministrator: true);
        }

        var login = AuthenticationService.Login(username, password);
        if (!login.Success || login.User is null)
        {
            SetStatus(login.Message);
            return;
        }

        if (RememberMeCheckBox.IsChecked == true)
        {
            var configured = QuickAccessService.Configure(login.User, AccessPinBox.Password, ConfirmAccessPinBox.Password);
            if (!configured.Success)
            {
                SetStatus(configured.Message);
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

    private async Task OpenProviderAdministrationAsync(string password)
    {
        SubmitButton.IsEnabled = false;
        SetStatus("Lizenzverwaltung wird geöffnet…", success: true);
        try
        {
            await ProviderAdminService.SignInAsync(password);
            PasswordBox.Clear();
            SetStatus(string.Empty);
            var window = new ProviderAdminWindow { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordRecoveryWindow(UsernameBox.Text) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SetStatus("Passwort zurückgesetzt. Ein gespeicherter PIN-Zugang wurde aus Sicherheitsgründen ebenfalls aufgehoben.", success: true);
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

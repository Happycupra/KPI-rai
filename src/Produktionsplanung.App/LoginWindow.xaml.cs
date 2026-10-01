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
    private bool _updatingCompanyCode;
    private bool _companyCodeTouched;
    private int _failedPinAttempts;

    public LoginWindow()
    {
        InitializeComponent();
        _hasUsers = AuthenticationService.HasUsers();
        AddProviderAdminEntry();

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

    private void AddProviderAdminEntry()
    {
        if (SubmitButton.Parent is not System.Windows.Controls.StackPanel panel)
            return;

        var button = new System.Windows.Controls.Button
        {
            Content = "Anbieter-Verwaltung",
            Margin = new Thickness(0, 14, 0, 0),
            Style = (Style)FindResource("GhostButtonStyle"),
            ToolTip = "Online-Firmen und Lizenzen mit dem Anbieter-Konto verwalten"
        };
        button.Click += ProviderAdmin_Click;
        panel.Children.Add(button);
    }

    private void ProviderAdmin_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProviderAdminWindow { Owner = this };
        window.ShowDialog();
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
        DisplayNamePanel.Visibility = Visibility.Collapsed;
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
        {
            SetStatus("Auf dieser Installation ist noch kein Benutzerkonto vorhanden. Bitte zuerst „Registrieren“ wählen.");
        }
        else
        {
            SetStatus(string.Empty);
            UsernameBox.Focus();
        }
    }

    private void ApplyRegistrationMode()
    {
        if (_hasUsers)
        {
            var existingSettings = AppSettingsService.Load();
            var companyText = !string.IsNullOrWhiteSpace(existingSettings.CompanyName) &&
                              !string.Equals(existingSettings.CompanyName, "SolutionCompakt", StringComparison.OrdinalIgnoreCase)
                ? $" für „{existingSettings.CompanyName}“"
                : string.Empty;

            SetStatus($"Diese Installation ist bereits{companyText} registriert. Eine zweite Firma bzw. ein zweiter Firmenname kann hier nicht hinterlegt werden. Bitte ein bestehendes Konto verwenden.");
            UpdateModeButtonStyles();
            return;
        }

        _registrationMode = true;
        _quickAccessMode = false;
        _failedPinAttempts = 0;
        Title = "SolutionCompakt Registrierung";
        QuickAccessPanel.Visibility = Visibility.Collapsed;
        StandardLoginPanel.Visibility = Visibility.Visible;
        CompanyPanel.Visibility = Visibility.Visible;
        DisplayNamePanel.Visibility = Visibility.Visible;
        ConfirmPasswordPanel.Visibility = Visibility.Visible;
        ForgotPasswordButton.Visibility = Visibility.Collapsed;
        ModeTitle.Text = "Firma registrieren";
        ModeDescription.Text = "Firma einmalig registrieren und den ersten lokalen Administrator anlegen.";
        SubmitButton.Content = "Firma registrieren und anmelden";

        var current = AppSettingsService.Load();
        if (CompanyIdentityService.IsRegistered())
        {
            CompanyNameBox.Text = current.CompanyName;
            _updatingCompanyCode = true;
            CompanyCodeBox.Text = current.CompanyCode;
            _updatingCompanyCode = false;
            _companyCodeTouched = true;
            ModeDescription.Text = "Die Firmenangaben wurden bereits gespeichert. Schließe die Ersteinrichtung mit dem ersten Administratorkonto ab.";
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
            ModeDescription.Text = $"{company.CompanyName} · {company.CompanyCode}\nMit deinem lokalen Benutzerkonto anmelden.";
        }
        else
        {
            ModeDescription.Text = "Mit deinem lokalen Benutzerkonto anmelden.";
        }
    }

    private void CompanyNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_registrationMode || CompanyCodeBox is null || _companyCodeTouched)
            return;
        _updatingCompanyCode = true;
        CompanyCodeBox.Text = CompanyIdentityService.SuggestCode(CompanyNameBox.Text);
        CompanyCodeBox.CaretIndex = CompanyCodeBox.Text.Length;
        _updatingCompanyCode = false;
    }

    private void CompanyCodeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_registrationMode && !_updatingCompanyCode)
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
        ApplyLoginMode(preferQuickAccess: false);
        RememberMeCheckBox.IsChecked = true;
        PinSetupPanel.Visibility = Visibility.Visible;
        ModeTitle.Text = "Anmeldung mit Passwort";
        ApplyCompanyDescription();
        SubmitButton.Content = "Anmelden";
        PasswordBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
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

        if (!_registrationMode && !_hasUsers)
        {
            SetStatus("Auf dieser Installation ist noch kein Benutzerkonto vorhanden. Bitte zuerst „Registrieren“ wählen.");
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

            var created = AuthenticationService.CreateInitialAdministrator(
                CompanyNameBox.Text,
                CompanyCodeBox.Text,
                username,
                DisplayNameBox.Text,
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

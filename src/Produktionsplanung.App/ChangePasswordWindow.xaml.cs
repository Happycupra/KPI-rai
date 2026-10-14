using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    private async void Change_Click(object sender, RoutedEventArgs e)
    {
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            StatusBlock.Text = "Die neuen Passwörter stimmen nicht überein.";
            return;
        }

        if (FirebaseCompanyClient.Enabled)
        {
            try
            {
                var user = SessionService.CurrentUser!;
                await FirebaseCloudAdministration.SaveUserAsync(user.Id, user.Username, user.DisplayName, user.Role, true,
                    NewPasswordBox.Password, FirebaseCompanyClient.Profile!.UserSetVersion, true, CurrentPasswordBox.Password);
                QuickAccessService.Clear(); DialogResult = true; Close();
            }
            catch (Exception ex) { StatusBlock.Text = ex.Message; }
            return;
        }
        var result = AuthenticationService.ChangeOwnPassword(CurrentPasswordBox.Password, NewPasswordBox.Password);
        if (!result.Success)
        {
            StatusBlock.Text = result.Message;
            return;
        }

        MessageBox.Show(result.Message, "SolutionCompakt", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }
}

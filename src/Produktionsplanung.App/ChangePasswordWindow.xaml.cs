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

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            StatusBlock.Text = "Die neuen Passwörter stimmen nicht überein.";
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

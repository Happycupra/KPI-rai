using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class UserAdminView : UserControl
{
    private void NewPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is UserAdminViewModel viewModel)
            viewModel.NewPassword = NewPasswordBox.Password;
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is UserAdminViewModel viewModel)
            viewModel.ConfirmNewPassword = ConfirmPasswordBox.Password;
    }

    private void CreateQrCode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not UserAdminViewModel viewModel || viewModel.SelectedUser is null)
        {
            MessageBox.Show(
                "Bitte zuerst einen bestehenden Benutzer in der Liste auswählen.",
                "SolutionCompakt",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var settings = AppSettingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.CompanyCode))
        {
            MessageBox.Show(
                "Für diese Installation ist noch kein Firmen-Code hinterlegt.",
                "SolutionCompakt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = new UserQrCodeWindow(
            settings.CompanyName,
            settings.CompanyCode,
            viewModel.SelectedUser.Username,
            viewModel.SelectedUser.DisplayName)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    public UserAdminView()
    {
        InitializeComponent();
        var viewModel = new UserAdminViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserAdminViewModel.NewPassword) &&
                NewPasswordBox.Password != viewModel.NewPassword)
                NewPasswordBox.Password = viewModel.NewPassword;

            if (e.PropertyName == nameof(UserAdminViewModel.ConfirmNewPassword) &&
                ConfirmPasswordBox.Password != viewModel.ConfirmNewPassword)
                ConfirmPasswordBox.Password = viewModel.ConfirmNewPassword;
        };
    }
}

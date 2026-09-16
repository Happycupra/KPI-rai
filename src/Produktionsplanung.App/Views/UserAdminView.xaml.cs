using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class UserAdminView : UserControl
{
    private void NewPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is UserAdminViewModel viewModel)
            viewModel.NewPassword = NewPasswordBox.Password;
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
        };
    }
}

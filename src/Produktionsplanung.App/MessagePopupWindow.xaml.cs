using System.Windows;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class MessagePopupWindow : Window
{
    public UserMessageRow Message { get; }

    public MessagePopupWindow(UserMessageRow message)
    {
        Message = message;
        InitializeComponent();
        DataContext = message;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

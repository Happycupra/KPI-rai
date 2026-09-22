using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class BatchBrowserView : UserControl
{
    public BatchBrowserView() : this("Heute") { }
    public BatchBrowserView(string mode, int? articleId = null, bool archive = false)
    {
        InitializeComponent();
        DataContext = new BatchBrowserViewModel(mode, articleId, archive);
        ArchiveFilters.Visibility = archive ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => ((BatchBrowserViewModel)DataContext).Refresh();
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BatchRow row)
            (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Id);
    }
    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ((BatchBrowserViewModel)DataContext).SelectedBatch is not { } row) return;
        (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Id);
        e.Handled = true;
    }
    private void NewBatch_Click(object sender, RoutedEventArgs e)
    {
        var vm = (BatchBrowserViewModel)DataContext;
        (Window.GetWindow(this) as MainWindow)?.CreateBatch(vm.FixedArticleId ?? vm.SelectedArticle?.Id);
        vm.Refresh();
    }
}

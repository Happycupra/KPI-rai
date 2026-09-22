using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class BatchComparisonView : UserControl
{
    private readonly int articleId;
    public BatchComparisonView(int articleId)
    {
        this.articleId = articleId;
        InitializeComponent();
        Limit.ItemsSource = new[] { 10, 25, 50 };
        Limit.SelectedItem = 10;
        Loaded += (_, _) => Refresh();
        Refresh();
    }
    public void Refresh()
    {
        if (!IsInitialized) return;
        try
        {
            var selected = (ComparisonGrid.SelectedItem as BatchComparisonRow)?.Batch.Id;
            var rows = BatchComparisonService.Load(articleId, Limit.SelectedItem is int count ? count : 10);
            ComparisonGrid.ItemsSource = rows;
            ComparisonGrid.SelectedItem = rows.FirstOrDefault(x => x.Batch.Id == selected);
            Message.Text = rows.Count == 0 ? "Noch keine abgeschlossenen Chargen für diesen Artikel." : $"{rows.Count} Charge(n) | Stand {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) { ComparisonGrid.ItemsSource = null; Message.Text = ex.Message; }
    }
    private void Limit_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Refresh(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BatchComparisonRow row)
            (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Batch.Id);
    }
    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ComparisonGrid.SelectedItem is not BatchComparisonRow row) return;
        (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Batch.Id);
        e.Handled = true;
    }
}

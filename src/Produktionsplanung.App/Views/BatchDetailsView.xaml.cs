using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class BatchDetailsView : UserControl
{
    private readonly int id;
    public BatchDetailsView(int orderId)
    {
        id = orderId;
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }
    public void Refresh()
    {
        try
        {
            var details = BatchService.GetDetails(id);
            DataContext = details;
            var closed = details.Batch.Status == "Abgeschlossen";
            EditActions.Visibility = SessionService.IsPlannerOrAdmin && !closed ? Visibility.Visible : Visibility.Collapsed;
            ReopenActions.Visibility = SessionService.IsPlannerOrAdmin && closed ? Visibility.Visible : Visibility.Collapsed;
            CardsEmpty.Visibility = details.JobCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActualsEmpty.Visibility = details.Actuals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActualsGrid.ItemsSource = details.Actuals.Select(x => new { x.Date, Article = x.ArticleNumberSnapshot ?? "Nicht erfasst",
                Batch = string.IsNullOrWhiteSpace(x.BatchNumber) ? "Nicht erfasst" : x.BatchNumber,
                Good = x.GoodQuantity, Scrap = x.ScrapQuantity, Downtime = x.Downtimes.Sum(d => d.Minutes),
                Note = !string.IsNullOrWhiteSpace(x.BatchNumber) && x.BatchNumber != details.Batch.BatchNumber ? "Abweichende historische Charge" : "" }).ToList();
        }
        catch (Exception ex) { Message.Text = ex.Message; EditActions.IsEnabled = ReopenActions.IsEnabled = false; }
    }
    private MainWindow? Host => Window.GetWindow(this) as MainWindow;
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Chargenbericht speichern", Filter = "PDF-Dokument (*.pdf)|*.pdf", DefaultExt = ".pdf",
            AddExtension = true, OverwritePrompt = true, FileName = BatchReportPdfService.BuildFileName(id)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var result = BatchReportPdfService.Export(id, dialog.FileName);
            Message.Text = $"PDF gespeichert ({result.PageCount} Seite(n)): {result.FilePath}";
        }
        catch (Exception ex) { Message.Text = $"PDF konnte nicht gespeichert werden: {ex.Message}"; }
    }
    private void Edit_Click(object sender, RoutedEventArgs e) => Host?.OpenProductionOrder(id);
    private void Control_Click(object sender, RoutedEventArgs e) => Host?.OpenManufacturingControl(id);
    private void Actual_Click(object sender, RoutedEventArgs e) => Host?.OpenProductionActual(id);
    private void Complete_Click(object sender, RoutedEventArgs e) => Run(() => BatchService.Complete(id));
    private void Reopen_Click(object sender, RoutedEventArgs e) => Run(() => BatchService.Reopen(id, Reason.Text));
    private void Run(Action action)
    {
        try { action(); Reason.Clear(); Refresh(); Message.Text = "Charge aktualisiert."; }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
}

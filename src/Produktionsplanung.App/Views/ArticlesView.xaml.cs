using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.Views;

public partial class ArticlesView : UserControl
{
    public ArticlesView()
    {
        InitializeComponent();
        EditActions.IsEnabled = SessionService.IsPlannerOrAdmin;
        Loaded += (_, _) => Refresh();
    }
    private ArticleMaster? Selected => ArticlesGrid.SelectedItem as ArticleMaster;
    private void Refresh(int? select = null)
    {
        if (!IsInitialized) return;
        var previousHistory = History.Content as BatchBrowserView;
        var previousId = Selected?.Id;
        var id = select ?? previousId;
        var rows = ArticleService.Search(Search.Text, ActiveOnly.IsChecked == true);
        ArticlesGrid.ItemsSource = rows;
        ArticlesGrid.SelectedItem = rows.FirstOrDefault(x => x.Id == id);
        if (previousHistory is not null && Selected?.Id == previousId)
        {
            History.Content = previousHistory;
            ((Produktionsplanung.App.ViewModels.BatchBrowserViewModel)previousHistory.DataContext).Refresh();
        }
        Message.Text = rows.Count == 0 ? "Noch keine passenden Artikel. Mit „Artikel anlegen“ beginnen." : $"{rows.Count} Artikel";
    }
    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) Refresh(); }
    private void Active_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Refresh(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void New_Click(object sender, RoutedEventArgs e) => Edit(null);
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Selected is { } a) Edit(a.Id); }
    private void Edit(int? id)
    {
        var dialog = new ArticleEditorWindow(id) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Refresh(dialog.SavedId);
    }
    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } a) return;
        try { ArticleService.Deactivate(a.Id); Refresh(); }
        catch (Exception ex) { Message.Text = ex.Message; }
    }
    private void NewBatch_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } a) { Message.Text = "Bitte zuerst einen Artikel auswählen."; return; }
        (Window.GetWindow(this) as MainWindow)?.CreateBatch(a.Id);
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } a) { History.Content = null; return; }
        HistoryTitle.Text = $"Chargen · {a.ArticleNumber} – {a.Name}";
        History.Content = new BatchBrowserView("Alle", a.Id);
    }
}

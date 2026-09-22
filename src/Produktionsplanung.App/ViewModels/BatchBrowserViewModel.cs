using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class BatchBrowserViewModel : ObservableObject
{
    public ObservableCollection<BatchRow> Rows { get; } = new();
    public ObservableCollection<ArticleMaster> Articles { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "Heute", "Alle offenen", "Laufend", "Probleme", "Abgeschlossen", "Alle" };
    [ObservableProperty] private string mode = "Heute";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private ArticleMaster? selectedArticle;
    [ObservableProperty] private DateTime? completedFrom;
    [ObservableProperty] private DateTime? completedTo;
    [ObservableProperty] private BatchRow? selectedBatch;
    [ObservableProperty] private string message = "";
    public bool CanEdit => SessionService.IsPlannerOrAdmin;
    public bool IsEmpty => Rows.Count == 0;
    public int? FixedArticleId { get; }
    public bool IsArchive { get; }
    public bool CanChooseMode => !IsArchive;

    public BatchBrowserViewModel(string initialMode = "Heute", int? articleId = null, bool archive = false)
    {
        FixedArticleId = articleId;
        IsArchive = archive;
        mode = archive ? "Abgeschlossen" : initialMode;
        Articles.Add(new ArticleMaster { Id = 0, Name = "Alle Artikel" });
        foreach (var article in ArticleService.Search()) Articles.Add(article);
        Refresh();
    }

    partial void OnModeChanged(string value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedArticleChanged(ArticleMaster? value) => Refresh();
    partial void OnCompletedFromChanged(DateTime? value) => Refresh();
    partial void OnCompletedToChanged(DateTime? value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var id = SelectedBatch?.Id;
            var rows = BatchService.Search(new(IsArchive ? "Abgeschlossen" : Mode, SearchText,
                FixedArticleId ?? (SelectedArticle?.Id > 0 ? SelectedArticle.Id : null), CompletedFrom, CompletedTo));
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            SelectedBatch = Rows.FirstOrDefault(x => x.Id == id);
            Message = $"{Rows.Count} Charge(n) · aktualisiert {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) { Rows.Clear(); Message = ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
    }
}

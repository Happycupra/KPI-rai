using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class AnalyticsViewModel
{
    public ObservableCollection<OeeWorkstationRow> OeeWorkstationRows { get; } = new();

    [ObservableProperty] private int oeeRecords;
    [ObservableProperty] private string oeeTotalQuantityText = "—";
    [ObservableProperty] private string oeeGoodQuantityText = "—";
    [ObservableProperty] private string oeeScrapQuantityText = "—";
    [ObservableProperty] private double oeeDowntimeMinutes;
    [ObservableProperty] private double oeeAvailabilityPercent;
    [ObservableProperty] private double oeePerformancePercent;
    [ObservableProperty] private double oeeQualityPercent;
    [ObservableProperty] private double oeePercent;
    [ObservableProperty] private string oeeDataState = "Noch keine Ist-Daten im Zeitraum.";

    public string OeeAvailabilityText => $"{OeeAvailabilityPercent:N1} %";
    public string OeePerformanceText => $"{OeePerformancePercent:N1} %";
    public string OeeQualityText => $"{OeeQualityPercent:N1} %";
    public string OeeText => $"{OeePercent:N1} %";
    public string OeeDowntimeText => $"{OeeDowntimeMinutes:N0} min";

    public void RefreshOeeAnalytics()
    {
        var (start, end) = GetPeriod();
        var summary = OeeAnalyticsService.LoadPeriod(start, end);

        OeeRecords = summary.Records;
        OeeTotalQuantityText = summary.TotalQuantityText;
        OeeGoodQuantityText = summary.GoodQuantityText;
        OeeScrapQuantityText = summary.ScrapQuantityText;
        OeeDowntimeMinutes = summary.DowntimeMinutes;
        OeeAvailabilityPercent = summary.AvailabilityPercent;
        OeePerformancePercent = summary.PerformancePercent;
        OeeQualityPercent = summary.QualityPercent;
        OeePercent = summary.OeePercent;
        OeeDataState = summary.Records == 0
            ? "Noch keine Ist-Daten im gewählten Zeitraum."
            : $"{summary.Records} Ist-Erfassung(en) im gewählten Zeitraum.";

        OeeWorkstationRows.Clear();
        foreach (var row in summary.WorkstationRows)
            OeeWorkstationRows.Add(row);

        OnPropertyChanged(nameof(OeeAvailabilityText));
        OnPropertyChanged(nameof(OeePerformanceText));
        OnPropertyChanged(nameof(OeeQualityText));
        OnPropertyChanged(nameof(OeeText));
        OnPropertyChanged(nameof(OeeTotalQuantityText));
        OnPropertyChanged(nameof(OeeScrapQuantityText));
        OnPropertyChanged(nameof(OeeDowntimeText));
    }
}

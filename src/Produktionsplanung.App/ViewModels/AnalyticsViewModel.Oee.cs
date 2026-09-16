using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class AnalyticsViewModel
{
    public ObservableCollection<OeeWorkstationRow> OeeWorkstationRows { get; } = new();

    [ObservableProperty] private int oeeRecords;
    [ObservableProperty] private double oeeTotalQuantity;
    [ObservableProperty] private double oeeGoodQuantity;
    [ObservableProperty] private double oeeScrapQuantity;
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
    public string OeeTotalQuantityText => $"{OeeTotalQuantity:N0}";
    public string OeeScrapQuantityText => $"{OeeScrapQuantity:N0}";
    public string OeeDowntimeText => $"{OeeDowntimeMinutes:N0} min";

    public void RefreshOeeAnalytics()
    {
        var (start, end) = GetPeriod();
        var summary = OeeAnalyticsService.LoadPeriod(start, end);

        OeeRecords = summary.Records;
        OeeTotalQuantity = summary.TotalQuantity;
        OeeGoodQuantity = summary.GoodQuantity;
        OeeScrapQuantity = summary.ScrapQuantity;
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

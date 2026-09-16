using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.ViewModels;

public partial class ShiftManagementViewModel : ObservableObject
{
    public ObservableCollection<Shift> Shifts { get; } = new();

    [ObservableProperty] private Shift? selectedShift;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string startTimeText = "06:00";
    [ObservableProperty] private string endTimeText = "14:00";
    [ObservableProperty] private int breakMinutes = 30;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ShiftManagementViewModel() => Load();

    partial void OnSelectedShiftChanged(Shift? value)
    {
        if (value is null) return;
        Name = value.Name;
        StartTimeText = value.StartTime.ToString(@"hh\:mm");
        EndTimeText = value.EndTime.ToString(@"hh\:mm");
        BreakMinutes = value.BreakMinutes;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NewShift()
    {
        SelectedShift = null;
        Name = string.Empty;
        StartTimeText = "06:00";
        EndTimeText = "14:00";
        BreakMinutes = 30;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "Bitte einen Schichtnamen eingeben.";
            return;
        }

        if (!TimeSpan.TryParse(StartTimeText, out var start) || !TimeSpan.TryParse(EndTimeText, out var end))
        {
            StatusMessage = "Zeit bitte im Format HH:mm eingeben.";
            return;
        }

        if (BreakMinutes < 0 || BreakMinutes > 240)
        {
            StatusMessage = "Pause muss zwischen 0 und 240 Minuten liegen.";
            return;
        }

        using var db = new AppDbContext();
        Shift entity;
        if (SelectedShift is null)
        {
            entity = new Shift();
            db.Shifts.Add(entity);
        }
        else
        {
            entity = db.Shifts.First(x => x.Id == SelectedShift.Id);
        }

        entity.Name = Name.Trim();
        entity.StartTime = start;
        entity.EndTime = end;
        entity.BreakMinutes = BreakMinutes;
        db.SaveChanges();

        Load(entity.Id);
        StatusMessage = "Schicht gespeichert.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedShift is null) return;
        using var db = new AppDbContext();
        var id = SelectedShift.Id;
        if (db.PlanningAssignments.Any(x => x.ShiftId == id))
        {
            StatusMessage = "Schicht kann nicht gelöscht werden, da Planungen vorhanden sind.";
            return;
        }

        var entity = db.Shifts.First(x => x.Id == id);
        db.Shifts.Remove(entity);
        db.SaveChanges();
        Load();
        NewShift();
        StatusMessage = "Schicht gelöscht.";
    }

    private void Load(int? selectId = null)
    {
        using var db = new AppDbContext();
        var items = db.Shifts.AsNoTracking().AsEnumerable().OrderBy(x => x.StartTime).ToList();
        Shifts.Clear();
        foreach (var item in items) Shifts.Add(item);
        SelectedShift = selectId is null ? null : Shifts.FirstOrDefault(x => x.Id == selectId);
    }
}

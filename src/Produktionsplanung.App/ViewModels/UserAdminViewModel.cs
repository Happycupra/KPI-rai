using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class UserAdminViewModel : ObservableObject
{
    public ObservableCollection<UserAdminRow> Users { get; } = new();
    public ObservableCollection<AuditLogRow> AuditRows { get; } = new();
    public IReadOnlyList<string> RoleOptions { get; } = UserRoles.All;
    public ObservableCollection<UserEmployeeOption> EmployeeOptions { get; } = new();

    [ObservableProperty] private UserAdminRow? selectedUser;
    [ObservableProperty] private UserEmployeeOption? selectedEmployeeLink;
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string selectedRole = UserRoles.Observer;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmNewPassword = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public UserAdminViewModel()
    {
        LoadEmployeeOptions();
        Refresh();
        NewUser();
    }

    public string EditorTitle => SelectedUser is null
        ? "Neuen Benutzer anlegen"
        : $"Benutzer bearbeiten · {SelectedUser.Username}";

    partial void OnSelectedUserChanged(UserAdminRow? value)
    {
        OnPropertyChanged(nameof(EditorTitle));
        if (value is null) return;
        Username = value.Username;
        DisplayName = value.DisplayName;
        SelectedRole = value.Role;
        IsActive = value.IsActive;
        SelectedEmployeeLink = EmployeeOptions.FirstOrDefault(x => x.Id == value.EmployeeId) ?? EmployeeOptions.FirstOrDefault();
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NewUser()
    {
        SelectedUser = null;
        Username = string.Empty;
        DisplayName = string.Empty;
        SelectedRole = UserRoles.Observer;
        IsActive = true;
        SelectedEmployeeLink = EmployeeOptions.FirstOrDefault();
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private void SaveUser()
    {
        if (!SessionService.IsAdministrator)
        {
            StatusMessage = "Nur Administratoren dürfen Benutzer verwalten.";
            return;
        }

        var normalizedUsername = Username.Trim();
        if (normalizedUsername.Length < 3)
        {
            StatusMessage = "Der Benutzername muss mindestens 3 Zeichen lang sein.";
            return;
        }
        if (!UserRoles.All.Contains(SelectedRole))
        {
            StatusMessage = "Bitte eine gültige Rolle auswählen.";
            return;
        }

        using var db = new AppDbContext();
        var editingId = SelectedUser?.Id;
        var invalidateQuickAccess = editingId.HasValue &&
            (!IsActive || !string.IsNullOrWhiteSpace(NewPassword) || !string.IsNullOrWhiteSpace(ConfirmNewPassword));
        if (db.UserAccounts.AsNoTracking().Any(x => x.Username == normalizedUsername && (!editingId.HasValue || x.Id != editingId.Value)))
        {
            StatusMessage = "Dieser Benutzername existiert bereits.";
            return;
        }

        if (SelectedUser is null)
        {
            if (NewPassword != ConfirmNewPassword)
            {
                StatusMessage = "Die Passwörter stimmen nicht überein.";
                return;
            }

            var validation = PasswordService.ValidatePassword(NewPassword);
            if (validation is not null)
            {
                StatusMessage = validation;
                return;
            }

            var (hash, salt) = PasswordService.HashPassword(NewPassword);
            db.UserAccounts.Add(new UserAccount
            {
                Username = normalizedUsername,
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? normalizedUsername : DisplayName.Trim(),
                Role = SelectedRole,
                IsActive = IsActive,
                EmployeeId = SelectedEmployeeLink?.Id,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            var entity = db.UserAccounts.First(x => x.Id == SelectedUser.Id);
            if (SessionService.CurrentUser?.Id == entity.Id && (!IsActive || SelectedRole != UserRoles.Administrator))
            {
                StatusMessage = "Der aktuell angemeldete Administrator kann sich nicht selbst deaktivieren oder die eigene Admin-Rolle entfernen.";
                return;
            }

            if (entity.Role == UserRoles.Administrator && (SelectedRole != UserRoles.Administrator || !IsActive))
            {
                var activeAdmins = db.UserAccounts.Count(x => x.Role == UserRoles.Administrator && x.IsActive);
                if (activeAdmins <= 1)
                {
                    StatusMessage = "Mindestens ein aktiver Administrator muss erhalten bleiben.";
                    return;
                }
            }

            entity.Username = normalizedUsername;
            entity.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? normalizedUsername : DisplayName.Trim();
            entity.Role = SelectedRole;
            entity.IsActive = IsActive;
            entity.EmployeeId = SelectedEmployeeLink?.Id;

            if (!string.IsNullOrWhiteSpace(NewPassword) || !string.IsNullOrWhiteSpace(ConfirmNewPassword))
            {
                if (NewPassword != ConfirmNewPassword)
                {
                    StatusMessage = "Die Passwörter stimmen nicht überein.";
                    return;
                }

                var validation = PasswordService.ValidatePassword(NewPassword);
                if (validation is not null)
                {
                    StatusMessage = validation;
                    return;
                }
                var (hash, salt) = PasswordService.HashPassword(NewPassword);
                entity.PasswordHash = hash;
                entity.PasswordSalt = salt;
            }
        }

        try
        {
            db.SaveChanges();
            if (editingId.HasValue && invalidateQuickAccess)
                QuickAccessService.ClearIfUser(editingId.Value);
            var savedUsername = normalizedUsername;
            LoadUsers();
            SelectedUser = Users.FirstOrDefault(x =>
                string.Equals(x.Username, savedUsername, StringComparison.Ordinal));
            LoadAudit();
            StatusMessage = "Benutzer gespeichert.";
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
            OnPropertyChanged(nameof(EditorTitle));
        }
        catch (DbUpdateException ex)
        {
            StatusMessage = $"Benutzer konnte nicht gespeichert werden: {ex.GetBaseException().Message}";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadEmployeeOptions();
        LoadUsers();
        LoadAudit();
        StatusMessage = string.Empty;
    }

    private void LoadEmployeeOptions()
    {
        var selectedId = SelectedEmployeeLink?.Id;
        using var db = new AppDbContext();
        EmployeeOptions.Clear();
        EmployeeOptions.Add(new UserEmployeeOption { Id = null, DisplayName = "– nicht verknüpft –" });
        foreach (var employee in db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
        {
            EmployeeOptions.Add(new UserEmployeeOption
            {
                Id = employee.Id,
                DisplayName = $"{employee.LastName}, {employee.FirstName} · {employee.PersonnelNumber}"
            });
        }
        SelectedEmployeeLink = EmployeeOptions.FirstOrDefault(x => x.Id == selectedId) ?? EmployeeOptions.FirstOrDefault();
    }

    private void LoadUsers()
    {
        using var db = new AppDbContext();
        var selectedId = SelectedUser?.Id;
        var rows = db.UserAccounts.AsNoTracking().OrderBy(x => x.Username).ToList();
        var employeeMap = db.Employees.IgnoreQueryFilters().AsNoTracking().ToDictionary(x => x.Id);
        Users.Clear();
        foreach (var x in rows)
        {
            employeeMap.TryGetValue(x.EmployeeId ?? 0, out var linkedEmployee);
            Users.Add(new UserAdminRow
            {
                Id = x.Id,
                EmployeeId = x.EmployeeId,
                EmployeeName = linkedEmployee is null ? "–" : $"{linkedEmployee.LastName}, {linkedEmployee.FirstName}",
                Username = x.Username,
                DisplayName = x.DisplayName,
                Role = x.Role,
                IsActive = x.IsActive,
                LastLoginAtUtc = x.LastLoginAtUtc
            });
        }
        SelectedUser = selectedId.HasValue ? Users.FirstOrDefault(x => x.Id == selectedId) : null;
    }

    private void LoadAudit()
    {
        using var db = new AppDbContext();
        var rows = db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.TimestampUtc)
            .Take(500)
            .ToList();

        AuditRows.Clear();
        foreach (var x in rows)
        {
            AuditRows.Add(new AuditLogRow
            {
                Id = x.Id,
                TimestampUtc = x.TimestampUtc,
                Username = x.Username,
                Action = x.Action,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                Details = x.Details
            });
        }
    }
}

public class UserAdminRow
{
    public int Id { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public string LastLoginText => LastLoginAtUtc.HasValue ? LastLoginAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "–";
}

public class AuditLogRow
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string TimestampText => TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}


public class UserEmployeeOption
{
    public int? Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

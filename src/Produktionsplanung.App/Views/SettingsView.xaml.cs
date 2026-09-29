using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class SettingsView : UserControl, IUnsavedChangesAware
{
    private SettingsViewModel viewModel;
    private string baseline = string.Empty;
    private FrameworkElement? dashboardHost;
    private FrameworkElement? detailsHost;
    private TabControl? settingsTabs;

    public SettingsView()
    {
        InitializeComponent();
        viewModel = CreateViewModel();
        DataContext = viewModel;
        BuildSettingsDashboard();
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Systemeinstellungen";

    public bool TrySaveChanges()
    {
        viewModel.SaveSettingsCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        viewModel = CreateViewModel();
        DataContext = viewModel;
        CaptureBaseline();
    }

    private SettingsViewModel CreateViewModel()
    {
        var vm = new SettingsViewModel();
        vm.PropertyChanged += ViewModel_PropertyChanged;
        return vm;
    }

    private void BuildSettingsDashboard()
    {
        if (Content is not FrameworkElement existingSettings)
            return;

        settingsTabs = FindChild<TabControl>(existingSettings);
        Content = null;

        var shell = new Grid();
        dashboardHost = CreateDashboard();
        detailsHost = CreateDetailsHost(existingSettings);
        detailsHost.Visibility = Visibility.Collapsed;

        shell.Children.Add(dashboardHost);
        shell.Children.Add(detailsHost);
        Content = shell;
    }

    private FrameworkElement CreateDashboard()
    {
        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 28) };
        root.Children.Add(new TextBlock
        {
            Text = "Einstellungen & Verwaltung",
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Stammdaten, Benutzer und Systemeinstellungen zentral verwalten.",
            FontSize = 13,
            Foreground = ResourceBrush("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 24)
        });

        root.Children.Add(CreateSectionTitle("STAMMDATEN", "Grundlagen für Planung und Produktion"));
        var masterData = new WrapPanel { Margin = new Thickness(0, 8, 0, 24) };
        masterData.Children.Add(CreateCard("🏷️", "Artikelstamm", "Artikel, Standardmengen und Chargenbezug", "Einstellungen › Stammdaten › Artikel", () => OpenArea("articles")));
        masterData.Children.Add(CreateCard("👥", "Mitarbeitende & Skills", "Personal, Pensum und Qualifikationen", "Einstellungen › Stammdaten › Mitarbeitende", () => OpenArea("employees")));
        masterData.Children.Add(CreateCard("⚙️", "Arbeitsplätze & Schichten", "Arbeitsplätze, Schichtmodelle und Besetzung", "Einstellungen › Stammdaten › Arbeitsplätze", () => OpenArea("workstations")));
        root.Children.Add(masterData);

        if (SessionService.IsAdministrator)
        {
            root.Children.Add(CreateSectionTitle("BENUTZER & ORGANISATION", "Administration und Unternehmensdaten"));
            var organisation = new WrapPanel { Margin = new Thickness(0, 8, 0, 24) };
            organisation.Children.Add(CreateCard("👤", "Benutzer & Audit", "Benutzerkonten, Rollen und Audit-Protokoll", "Einstellungen › Benutzer & Organisation › Benutzer", () => OpenArea("users")));
            organisation.Children.Add(CreateCard("🏢", "Unternehmen / Werk", "Firmenname, Firmen-Code und Standort", "Einstellungen › Benutzer & Organisation › Unternehmen", () => ShowSettingsTab(0)));
            root.Children.Add(organisation);

            root.Children.Add(CreateSectionTitle("DATEN & SICHERHEIT", "Datenschutz, Sicherung und Wiederherstellung"));
            var data = new WrapPanel { Margin = new Thickness(0, 8, 0, 24) };
            data.Children.Add(CreateCard("💾", "Backup & Wiederherstellung", "Backups erstellen, aufbewahren und zurückspielen", "Einstellungen › Daten & Sicherheit › Backup", () => ShowSettingsTab(1)));
            data.Children.Add(CreateCard("🗑️", "Papierkorb", "Gelöschte Datensätze nachvollziehen und wiederherstellen", "Einstellungen › Daten & Sicherheit › Papierkorb", () => ShowSettingsTab(2)));
            data.Children.Add(CreateCard("🔐", "Sicherheit", "Recovery-Code und automatische Sitzungssperre", "Einstellungen › Daten & Sicherheit › Sicherheit", () => ShowSettingsTab(3)));
            root.Children.Add(data);

            root.Children.Add(CreateSectionTitle("SYSTEM", "Export und persönliche Oberfläche"));
            var system = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            system.Children.Add(CreateCard("📤", "Export", "CSV-Export und Exportverzeichnis", "Einstellungen › System › Export", () => ShowSettingsTab(4)));
            system.Children.Add(CreateCard("🎨", "Benutzeroberfläche", "Persönliche Ansicht und Navigationsoptionen", "Einstellungen › System › Benutzeroberfläche", () => ShowSettingsTab(5)));
            root.Children.Add(system);
        }
        else
        {
            var note = new Border
            {
                Background = ResourceBrush("InfoSoftBrush"),
                BorderBrush = ResourceBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 4, 0, 0)
            };
            note.Child = new TextBlock
            {
                Text = "System- und Benutzerverwaltung ist Administratoren vorbehalten.",
                Foreground = ResourceBrush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(note);
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    private FrameworkElement CreateDetailsHost(FrameworkElement existingSettings)
    {
        var dock = new DockPanel();
        var back = new Button
        {
            Content = "←  Einstellungen-Übersicht",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(26, 16, 0, 0),
            Padding = new Thickness(14, 7, 14, 7),
            Style = TryFindResource("SecondaryButtonStyle") as Style
        };
        back.Click += (_, _) => ShowDashboard();
        DockPanel.SetDock(back, Dock.Top);
        dock.Children.Add(back);
        dock.Children.Add(existingSettings);
        return dock;
    }

    private FrameworkElement CreateSectionTitle(string title, string subtitle)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("MutedTextBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = ResourceBrush("MutedTextBrush"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        return panel;
    }

    private Button CreateCard(string icon, string title, string description, string path, Action action)
    {
        var content = new Grid { Margin = new Thickness(2) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var iconText = new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(iconText, 0);
        content.Children.Add(iconText);

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush")
        };
        Grid.SetRow(titleText, 1);
        content.Children.Add(titleText);

        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = ResourceBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 8)
        };
        Grid.SetRow(descriptionText, 2);
        content.Children.Add(descriptionText);

        var pathText = new TextBlock
        {
            Text = path,
            FontSize = 10,
            Foreground = ResourceBrush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(pathText, 3);
        content.Children.Add(pathText);

        var button = new Button
        {
            Width = 270,
            MinHeight = 142,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(16, 14, 16, 14),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = ResourceBrush("CardBrush"),
            BorderBrush = ResourceBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Content = content
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void OpenArea(string areaKey)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenTourArea(areaKey);
    }

    internal void ShowSettingsTab(int index)
    {
        if (!SessionService.IsAdministrator || dashboardHost is null || detailsHost is null || settingsTabs is null)
            return;

        settingsTabs.SelectedIndex = Math.Clamp(index, 0, Math.Max(0, settingsTabs.Items.Count - 1));
        dashboardHost.Visibility = Visibility.Collapsed;
        detailsHost.Visibility = Visibility.Visible;
    }

    internal void ShowDashboard()
    {
        if (dashboardHost is null || detailsHost is null)
            return;
        detailsHost.Visibility = Visibility.Collapsed;
        dashboardHost.Visibility = Visibility.Visible;
    }

    private Brush ResourceBrush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Transparent;

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            var nested = FindChild<T>(child);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.StatusMessage) &&
            viewModel.StatusMessage == "Einstellungen gespeichert.")
            CaptureBaseline();
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.CompanyName,
        viewModel.SiteName,
        viewModel.DefaultBackupDirectory,
        viewModel.DefaultExportDirectory,
        viewModel.AutoBackupOnExit,
        viewModel.BackupRetentionCount,
        viewModel.CsvDelimiter,
        viewModel.IncludeUtf8Bom,
        viewModel.AutoLockEnabled,
        viewModel.AutoLockMinutes);
}

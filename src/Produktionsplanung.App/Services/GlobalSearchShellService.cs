using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App.Services;

internal static class GlobalSearchShellService
{
    private static readonly ConditionalWeakTable<MainWindow, GlobalSearchController> Controllers = new();

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || Controllers.TryGetValue(window, out _))
            return;

        var controller = new GlobalSearchController(window);
        if (controller.Attach())
            Controllers.Add(window, controller);
    }

    private sealed class GlobalSearchController
    {
        private readonly MainWindow window;
        private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
        private TextBox searchBox = null!;
        private TextBlock placeholder = null!;
        private Popup popup = null!;
        private ListBox resultsList = null!;
        private TextBlock footer = null!;
        private IReadOnlyList<GlobalSearchResult> currentResults = Array.Empty<GlobalSearchResult>();

        public GlobalSearchController(MainWindow window)
        {
            this.window = window;
            searchTimer.Tick += SearchTimer_Tick;
        }

        public bool Attach()
        {
            if (window.FindName("CurrentPageTitle") is not TextBlock title ||
                title.Parent is not StackPanel titlePanel ||
                titlePanel.Parent is not Grid topbar)
                return false;

            var host = BuildSearchHost();
            Grid.SetColumn(host, 2);
            Panel.SetZIndex(host, 50);
            topbar.Children.Add(host);

            window.PreviewKeyDown += Window_PreviewKeyDown;
            window.Deactivated += (_, _) => popup.IsOpen = false;
            return true;
        }

        private Grid BuildSearchHost()
        {
            var host = new Grid
            {
                Width = 330,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 18, 0),
                ToolTip = "Global suchen (Ctrl+K)"
            };

            var searchChrome = new Border
            {
                Height = 38,
                CornerRadius = new CornerRadius(9),
                Background = BrushResource("SurfaceBrush", Color.FromRgb(248, 250, 252)),
                BorderBrush = BrushResource("BorderBrush", Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 8, 0)
            };

            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "🔍",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(icon, 0);
            inputGrid.Children.Add(icon);

            placeholder = new TextBlock
            {
                Text = "Suchen in SolutionCompakt…",
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Margin = new Thickness(1, 0, 0, 0)
            };
            Grid.SetColumn(placeholder, 1);
            inputGrid.Children.Add(placeholder);

            searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Height = 36,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = BrushResource("TextBrush", Color.FromRgb(15, 23, 42)),
                ToolTip = "Navigation, Einstellungen, Mitarbeitende, Artikel, Arbeitsplätze und Produktionsaufträge durchsuchen"
            };
            searchBox.TextChanged += SearchBox_TextChanged;
            searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
            searchBox.GotKeyboardFocus += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(searchBox.Text))
                    RunSearch();
            };
            Grid.SetColumn(searchBox, 1);
            inputGrid.Children.Add(searchBox);

            var shortcut = new Border
            {
                Background = BrushResource("CardBrush", Color.FromRgb(255, 255, 255)),
                BorderBrush = BrushResource("BorderBrush", Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            shortcut.Child = new TextBlock
            {
                Text = "Ctrl+K",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139))
            };
            Grid.SetColumn(shortcut, 2);
            inputGrid.Children.Add(shortcut);

            searchChrome.Child = inputGrid;
            host.Children.Add(searchChrome);

            popup = new Popup
            {
                PlacementTarget = host,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -150,
                VerticalOffset = 7,
                Width = 480,
                AllowsTransparency = true,
                StaysOpen = false
            };

            var popupBorder = new Border
            {
                Background = BrushResource("CardBrush", Colors.White),
                BorderBrush = BrushResource("BorderBrush", Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8)
            };

            var popupContent = new DockPanel();
            var heading = new Grid { Margin = new Thickness(8, 5, 8, 7) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock
            {
                Text = "Globale Suche",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushResource("TextBrush", Color.FromRgb(15, 23, 42))
            });
            var hint = new TextBlock
            {
                Text = "ESC schließen",
                FontSize = 10,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139))
            };
            Grid.SetColumn(hint, 1);
            heading.Children.Add(hint);
            DockPanel.SetDock(heading, Dock.Top);
            popupContent.Children.Add(heading);

            footer = new TextBlock
            {
                FontSize = 10,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(8, 7, 8, 5),
                Text = "Suchbegriff eingeben"
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            popupContent.Children.Add(footer);

            resultsList = new ListBox
            {
                MaxHeight = 430,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ScrollViewer = { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            resultsList.MouseLeftButtonUp += ResultsList_MouseLeftButtonUp;
            popupContent.Children.Add(resultsList);

            popupBorder.Child = popupContent;
            popup.Child = popupBorder;
            host.Children.Add(popup);
            return host;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                searchBox.Focus();
                searchBox.SelectAll();
                if (!string.IsNullOrWhiteSpace(searchBox.Text))
                    RunSearch();
                e.Handled = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            searchTimer.Stop();

            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                popup.IsOpen = false;
                resultsList.Items.Clear();
                currentResults = Array.Empty<GlobalSearchResult>();
                return;
            }

            searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            searchTimer.Stop();
            RunSearch();
        }

        private void RunSearch()
        {
            currentResults = GlobalSearchService.Search(searchBox.Text, 10);
            resultsList.Items.Clear();

            foreach (var result in currentResults)
                resultsList.Items.Add(CreateResultItem(result));

            if (resultsList.Items.Count > 0)
                resultsList.SelectedIndex = 0;

            footer.Text = currentResults.Count == 0
                ? "Keine Treffer · Suchbegriff anpassen"
                : $"{currentResults.Count} Treffer · ↑/↓ auswählen · Enter öffnen";
            popup.IsOpen = true;
        }

        private ListBoxItem CreateResultItem(GlobalSearchResult result)
        {
            var item = new ListBoxItem
            {
                Tag = result,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                ToolTip = $"Öffnen · {result.Path}"
            };

            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = result.Icon,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 7, 0)
            };
            Grid.SetRowSpan(icon, 3);
            grid.Children.Add(icon);

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.Children.Add(new TextBlock
            {
                Text = result.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushResource("TextBrush", Color.FromRgb(15, 23, 42)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var category = new TextBlock
            {
                Text = result.Category,
                FontSize = 9,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(10, 1, 0, 0)
            };
            Grid.SetColumn(category, 1);
            titleRow.Children.Add(category);
            Grid.SetColumn(titleRow, 1);
            grid.Children.Add(titleRow);

            var subtitle = new TextBlock
            {
                Text = result.Subtitle,
                FontSize = 10,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0)
            };
            Grid.SetColumn(subtitle, 1);
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);

            var path = new TextBlock
            {
                Text = "⌖  " + result.Path,
                FontSize = 9,
                Foreground = BrushResource("MutedTextBrush", Color.FromRgb(100, 116, 139)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetColumn(path, 1);
            Grid.SetRow(path, 2);
            grid.Children.Add(path);

            item.Content = grid;
            return item;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && resultsList.Items.Count > 0)
            {
                resultsList.SelectedIndex = Math.Min(resultsList.Items.Count - 1, resultsList.SelectedIndex + 1);
                resultsList.ScrollIntoView(resultsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && resultsList.Items.Count > 0)
            {
                resultsList.SelectedIndex = Math.Max(0, resultsList.SelectedIndex - 1);
                resultsList.ScrollIntoView(resultsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                OpenSelectedResult();
                e.Handled = true;
            }
        }

        private void ResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (resultsList.SelectedItem is not null)
                OpenSelectedResult();
        }

        private void OpenSelectedResult()
        {
            if (resultsList.SelectedItem is not ListBoxItem item || item.Tag is not GlobalSearchResult result)
                return;

            popup.IsOpen = false;
            searchTimer.Stop();

            switch (result.TargetKind)
            {
                case GlobalSearchTargetKind.Area when !string.IsNullOrWhiteSpace(result.AreaKey):
                    window.OpenTourArea(result.AreaKey);
                    break;
                case GlobalSearchTargetKind.SettingsDashboard:
                    OpenSettings(null);
                    break;
                case GlobalSearchTargetKind.SettingsTab when result.SettingsTab.HasValue:
                    OpenSettings(result.SettingsTab.Value);
                    break;
                case GlobalSearchTargetKind.Employee when result.EntityId.HasValue:
                    window.OpenEmployee(result.EntityId.Value);
                    break;
                case GlobalSearchTargetKind.ProductionOrder when result.EntityId.HasValue:
                    window.OpenProductionOrder(result.EntityId.Value);
                    break;
                case GlobalSearchTargetKind.Batch when result.EntityId.HasValue:
                    window.OpenBatch(result.EntityId.Value);
                    break;
            }

            searchBox.Clear();
        }

        private void OpenSettings(int? tabIndex)
        {
            if (!SessionService.IsPlannerOrAdmin || (tabIndex.HasValue && !SessionService.IsAdministrator))
                return;

            try
            {
                var routeType = typeof(MainWindow).GetNestedType("NavigationRoute", BindingFlags.NonPublic);
                var createEntry = typeof(MainWindow).GetMethod("CreateEntry", BindingFlags.Instance | BindingFlags.NonPublic);
                var navigate = typeof(MainWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic);
                if (routeType is null || createEntry is null || navigate is null)
                    return;

                var route = Enum.Parse(routeType, "Settings");
                var entry = createEntry.Invoke(window, new object?[] { route, null, null, null });
                if (entry is null)
                    return;

                navigate.Invoke(window, new[] { entry, (object)true });
                if (window.FindName("ContentHost") is ContentControl contentHost && contentHost.Content is SettingsView settings)
                {
                    if (tabIndex.HasValue)
                        settings.ShowSettingsTab(tabIndex.Value);
                    else
                        settings.ShowDashboard();
                }
            }
            catch
            {
                // Search must never make the application unusable if a navigation target changes.
            }
        }

        private Brush BrushResource(string key, Color fallback)
        {
            try
            {
                return window.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
            }
            catch
            {
                return new SolidColorBrush(fallback);
            }
        }
    }
}

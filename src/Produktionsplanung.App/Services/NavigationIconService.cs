using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace Produktionsplanung.App.Services;

/// <summary>
/// Replaces the thin legacy MDL2 glyphs in the main navigation with a stronger,
/// rounded 24 px outline icon set. The icons are drawn locally with WPF geometry,
/// so the application does not gain another runtime or font dependency.
/// </summary>
internal static class NavigationIconService
{
    private const string AppliedMarker = "SolutionCompakt.ModernNavigationIcon";

    private static readonly IReadOnlyDictionary<string, string> IconData =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DashboardButton"] =
                "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",

            ["PlanningCalendarButton"] =
                "M6,3 V6 M18,3 V6 M4,8 H20 M5,5 H19 C20.1,5 21,5.9 21,7 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V7 C3,5.9 3.9,5 5,5 M7,12 H9 M11,12 H13 M15,12 H17 M7,16 H9 M11,16 H13 M15,16 H17",

            ["WorkTimeCalendarButton"] =
                "M12,3 C7.03,3 3,7.03 3,12 C3,16.97 7.03,21 12,21 C16.97,21 21,16.97 21,12 C21,7.03 16.97,3 12,3 Z M12,7 V12 L16,14",

            ["AbsencesButton"] =
                "M9,4 C6.8,4 5,5.8 5,8 C5,10.2 6.8,12 9,12 C11.2,12 13,10.2 13,8 C13,5.8 11.2,4 9,4 Z M3,20 C3.8,16.8 6,15 9,15 C11,15 12.7,15.8 13.8,17 M16,11 H21",

            ["BatchesButton"] =
                "M4,8 H20 V20 H4 Z M3,4 H21 V8 H3 Z M9,13 L11,15 L15,11",

            ["ProductionOrdersButton"] =
                "M8,5 H6 C4.9,5 4,5.9 4,7 V20 C4,21.1 4.9,22 6,22 H18 C19.1,22 20,21.1 20,20 V7 C20,5.9 19.1,5 18,5 H16 M9,3 H15 V7 H9 Z M8,12 H16 M8,16 H14",

            ["ManufacturingControlButton"] =
                "M3,4 H21 V17 H3 Z M8,21 H16 M12,17 V21 M7,12 L10,9 L13,12 L17,8",

            ["ProductionActualButton"] =
                "M4,20 V12 H8 V20 M10,20 V8 H14 V20 M16,20 V5 H20 V20 M3,20 H21",

            ["AnalyticsButton"] =
                "M4,20 V5 M4,20 H21 M7,16 L11,12 L14,15 L20,8 M17,8 H20 V11",

            ["ArticlesButton"] =
                "M4,7 L12,3 L20,7 L12,11 Z M4,7 V17 L12,21 L20,17 V7 M12,11 V21",

            ["EmployeesButton"] =
                "M9,4 C6.8,4 5,5.8 5,8 C5,10.2 6.8,12 9,12 C11.2,12 13,10.2 13,8 C13,5.8 11.2,4 9,4 Z M3,21 V19 C3,16.8 5.2,15 8,15 H10 C12.8,15 15,16.8 15,19 V21 M17,6 C18.7,6 20,7.3 20,9 C20,10.7 18.7,12 17,12 M17,15 C19.5,15 21,16.4 21,19 V21",

            ["WorkstationsButton"] =
                "M3,4 H21 V16 H3 Z M8,20 H16 M12,16 V20 M7,8 H11 V12 H7 Z M14,8 H18 V12 H14 Z",

            ["UserAdminButton"] =
                "M12,3 L20,6 V11 C20,16 16.6,19.2 12,21 C7.4,19.2 4,16 4,11 V6 Z M12,8 C10.3,8 9,9.3 9,11 C9,12.7 10.3,14 12,14 C13.7,14 15,12.7 15,11 C15,9.3 13.7,8 12,8 Z M8.5,17 C9.4,15.8 10.5,15.2 12,15.2 C13.5,15.2 14.6,15.8 15.5,17",

            ["SettingsButton"] =
                "M4,7 H8 M12,7 H20 M10,5 V9 M4,12 H13 M17,12 H20 M15,10 V14 M4,17 H6 M10,17 H20 M8,15 V19"
        };

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            handledEventsToo: true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Apply after all regular Loaded handlers so navigation rearrangement and templates are final.
        window.Dispatcher.BeginInvoke(new Action(() => Apply(window)), DispatcherPriority.Loaded);
    }

    private static void Apply(MainWindow window)
    {
        foreach (var (buttonName, geometryData) in IconData)
        {
            if (window.FindName(buttonName) is Button button)
                ApplyIcon(button, geometryData);
        }
    }

    private static void ApplyIcon(Button button, string geometryData)
    {
        button.ApplyTemplate();

        if (button.Template.FindName("IconChrome", button) is not Border iconChrome ||
            Equals(iconChrome.Tag, AppliedMarker))
            return;

        Geometry geometry;
        try
        {
            geometry = Geometry.Parse(geometryData);
            if (geometry.CanFreeze)
                geometry.Freeze();
        }
        catch
        {
            // Keep the original MDL2 glyph if a geometry ever becomes invalid.
            return;
        }

        var icon = new ShapePath
        {
            Data = geometry,
            Fill = Brushes.Transparent,
            StrokeThickness = 2.35,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        BindingOperations.SetBinding(
            icon,
            ShapePath.StrokeProperty,
            new Binding(nameof(Control.Foreground)) { Source = button, Mode = BindingMode.OneWay });

        var coordinateSpace = new Canvas
        {
            Width = 24,
            Height = 24,
            IsHitTestVisible = false
        };
        coordinateSpace.Children.Add(icon);

        var viewbox = new Viewbox
        {
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = coordinateSpace
        };

        iconChrome.Child = viewbox;
        iconChrome.Tag = AppliedMarker;
    }
}

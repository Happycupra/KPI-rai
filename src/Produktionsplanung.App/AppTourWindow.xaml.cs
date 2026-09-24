using System.Windows;
using System.Windows.Input;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class AppTourWindow : Window
{
    private readonly MainWindow mainWindow;
    private readonly IReadOnlyList<AppTourStep> steps;
    private int currentIndex;

    public AppTourWindow(MainWindow owner, int initialIndex = 0, bool automatic = false)
    {
        mainWindow = owner;
        steps = AppTourCatalog.GetAvailableSteps();
        currentIndex = Math.Clamp(initialIndex, 0, Math.Max(0, steps.Count - 1));

        InitializeComponent();
        Owner = owner;

        if (automatic)
        {
            AppSettingsService.UpdateCurrentUserPreferences(preferences =>
                preferences.AppTourLastShownVersion = AppTourCatalog.CurrentVersion);
        }

        PreviewKeyDown += OnPreviewKeyDown;
        RenderStep();
    }

    public void ShowStepForNavigation(string? buttonName)
    {
        var index = AppTourCatalog.FindStepIndex(buttonName);
        currentIndex = Math.Clamp(index, 0, Math.Max(0, steps.Count - 1));
        RenderStep();
        Activate();
    }

    private void RenderStep()
    {
        if (steps.Count == 0)
            return;

        var step = steps[currentIndex];
        StepIconText.Text = step.Icon;
        StepTitleText.Text = step.Title;
        StepSummaryText.Text = step.Summary;
        TipsList.ItemsSource = step.Tips;

        StepCounterText.Text = $"Schritt {currentIndex + 1} von {steps.Count}";
        TourProgress.Value = steps.Count <= 1 ? 1 : (double)(currentIndex + 1) / steps.Count;

        PreviousButton.IsEnabled = currentIndex > 0;
        OpenAreaButton.Visibility = string.IsNullOrWhiteSpace(step.AreaKey)
            ? Visibility.Collapsed
            : Visibility.Visible;
        OpenAreaHint.Visibility = OpenAreaButton.Visibility;

        var isLast = currentIndex == steps.Count - 1;
        NextButton.Content = isLast ? "Einführung abschliessen ✓" : "Weiter →";
        LaterButton.Visibility = isLast ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (currentIndex <= 0)
            return;
        currentIndex--;
        RenderStep();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (currentIndex < steps.Count - 1)
        {
            currentIndex++;
            RenderStep();
            return;
        }

        AppSettingsService.UpdateCurrentUserPreferences(preferences =>
        {
            preferences.AppTourLastShownVersion = AppTourCatalog.CurrentVersion;
            preferences.AppTourCompletedVersion = AppTourCatalog.CurrentVersion;
        });
        Close();
    }

    private void OpenArea_Click(object sender, RoutedEventArgs e)
    {
        var step = steps[currentIndex];
        if (string.IsNullOrWhiteSpace(step.AreaKey))
            return;

        mainWindow.OpenTourArea(step.AreaKey);
        Activate();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right)
        {
            Next_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            Previous_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

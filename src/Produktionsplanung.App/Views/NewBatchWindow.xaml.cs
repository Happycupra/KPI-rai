using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class NewBatchWindow : Window
{
    private readonly NewBatchViewModel plan;
    public int CreatedId { get; private set; }

    public NewBatchWindow(int? articleId = null)
    {
        InitializeComponent();
        try
        {
            plan = new NewBatchViewModel();
            DataContext = plan;

            using var db = new AppDbContext();
            var routings = db.ManufacturingRoutings.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
            routings.Insert(0, new ManufacturingRouting { Id = 0, Name = "Artikelvorgabe / ohne Arbeitsplan" });
            Routing.ItemsSource = routings;

            var articles = ArticleService.Search(activeOnly: true);
            Article.ItemsSource = articles;
            Article.SelectedItem = articleId.HasValue
                ? articles.FirstOrDefault(x => x.Id == articleId.Value)
                : articles.FirstOrDefault();

            if (articleId.HasValue && Article.SelectedItem is null)
                Message.Text = "Der ausgewählte Artikel ist nicht aktiv. Bitte einen aktiven Artikel auswählen.";
            else if (articles.Count == 0)
                Message.Text = "Bitte zuerst unter Artikel einen aktiven Artikel anlegen.";
            else if (!string.IsNullOrWhiteSpace(plan.PlanningError))
                Message.Text = plan.PlanningError;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Die Chargenerfassung konnte nicht initialisiert werden. Die bestehenden Daten wurden nicht verändert. " + ex.Message,
                ex);
        }
    }

    private void Article_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (Article.SelectedItem is not ArticleMaster article)
                return;

            plan.Quantity = article.DefaultQuantity ?? 1;
            plan.Unit = article.Unit;
            Routing.SelectedValue = article.DefaultRoutingId ?? 0;
        }
        catch (Exception ex)
        {
            Message.Text = "Artikel konnte nicht übernommen werden: " + ex.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Validation.GetHasError(QuantityInput) || Validation.GetHasError(ShiftCountInput) ||
                Validation.GetHasError(StaffInput) || Validation.GetHasError(PlanDate) || !PlanDate.SelectedDate.HasValue)
                throw new InvalidOperationException("Bitte gültige Mengen, Schichten, Personal und ein Datum eingeben.");

            if (Article.SelectedItem is not ArticleMaster article || plan.SelectedWorkstation is null || plan.SelectedShift is null)
                throw new InvalidOperationException("Artikel, Arbeitsplatz und eine freigegebene Startschicht auswählen.");

            if (!string.IsNullOrWhiteSpace(plan.PlanningError) && plan.RunSchedulePreview.Count < plan.PlannedShiftCount)
                throw new InvalidOperationException(plan.PlanningError);

            CreatedId = BatchService.CreateFromArticle(new(
                article.Id,
                plan.OrderNumber,
                plan.BatchNumber,
                plan.Quantity,
                plan.PlannedDate,
                plan.SelectedWorkstation.Id,
                plan.SelectedShift.Id,
                plan.PlannedShiftCount,
                plan.RequiredStaff,
                Routing.SelectedValue is int id && id > 0 ? id : null));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Message.Text = ex.Message;
        }
    }
}

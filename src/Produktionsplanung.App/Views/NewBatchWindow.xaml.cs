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
    private readonly NewBatchViewModel plan = new();
    public int CreatedId { get; private set; }

    public NewBatchWindow(int? articleId = null)
    {
        InitializeComponent();
        DataContext = plan;

        LoadRoutingsSafely();
        LoadArticlesSafely(articleId);

        if (!string.IsNullOrWhiteSpace(plan.PlanningError))
            AppendMessage(plan.PlanningError);
    }

    private void LoadRoutingsSafely()
    {
        try
        {
            using var db = new AppDbContext();
            var routings = db.ManufacturingRoutings.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();
            routings.Insert(0, new ManufacturingRouting { Id = 0, Name = "Artikelvorgabe / ohne Arbeitsplan" });
            Routing.ItemsSource = routings;
            Routing.SelectedValue = 0;
        }
        catch (Exception ex)
        {
            Routing.ItemsSource = new[] { new ManufacturingRouting { Id = 0, Name = "Ohne Arbeitsplan" } };
            Routing.SelectedValue = 0;
            AppendMessage("Arbeitspläne konnten nicht geladen werden: " + ex.Message);
        }
    }

    private void LoadArticlesSafely(int? articleId)
    {
        try
        {
            var articles = ArticleService.Search(activeOnly: true);
            Article.ItemsSource = articles;
            Article.SelectedItem = articleId.HasValue
                ? articles.FirstOrDefault(x => x.Id == articleId.Value)
                : articles.FirstOrDefault();

            if (articleId.HasValue && Article.SelectedItem is null)
                AppendMessage("Der ausgewählte Artikel ist nicht aktiv. Bitte einen aktiven Artikel auswählen.");
            else if (articles.Count == 0)
                AppendMessage("Bitte zuerst unter Artikel einen aktiven Artikel anlegen.");
        }
        catch (Exception ex)
        {
            Article.ItemsSource = Array.Empty<ArticleMaster>();
            AppendMessage("Artikel konnten nicht geladen werden: " + ex.Message);
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
            AppendMessage("Artikel konnte nicht übernommen werden: " + ex.Message);
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

    private void AppendMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        Message.Text = string.IsNullOrWhiteSpace(Message.Text)
            ? text
            : Message.Text + Environment.NewLine + text;
    }
}

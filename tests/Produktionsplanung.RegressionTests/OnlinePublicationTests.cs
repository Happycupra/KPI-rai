using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private static void DirectOnlinePublication()
    {
        var settings = AppSettingsService.Load();
        settings.OnlineWeekPlanEnabled = true;
        settings.CompanyId = "test-company";
        settings.CompanyCode = "SC-TEST";
        var snapshot = new OnlineWeekPlanSnapshot { CompanyId = settings.CompanyId, CompanyCode = settings.CompanyCode, WeekId = "2030-W03" };
        var requests = new List<string>();
        using var http = new HttpClient(new PublicationHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            requests.Add(request.RequestUri!.AbsolutePath);
            if (requests.Count == 1)
            {
                Check(json.RootElement.GetProperty("password").GetString() == " secret ", "Password whitespace changed");
                Check(json.RootElement.GetProperty("companyCode").GetString() == "SC-TEST", "Wrong tenant login");
                return JsonResponse("{\"customToken\":\"custom-test\"}");
            }
            if (requests.Count == 2)
            {
                Check(json.RootElement.GetProperty("token").GetString() == "custom-test", "Custom token not exchanged");
                Check(request.RequestUri.Host == "identitytoolkit.googleapis.com", "Unexpected token exchange host");
                return JsonResponse("{\"idToken\":\"id-test\"}");
            }
            Check(request.Headers.Authorization?.Parameter == "id-test", "Missing ID-token authorization");
            Check(json.RootElement.GetProperty("weekId").GetString() == "2030-W03", "Wrong week uploaded");
            return JsonResponse("{\"ok\":true,\"weekId\":\"2030-W03\",\"publishedAtUtc\":\"2030-01-14T09:15:30Z\"}");
        }));
        var receipt = OnlineWeekPlanPublisher.PublishAndRememberAsync(snapshot, "admin", " secret ", settings, http).GetAwaiter().GetResult();
        Check(requests.Count == 3, "Direct publication did not complete all stages");
        Check(receipt.PublishedAtUtc == new DateTime(2030, 1, 14, 9, 15, 30, DateTimeKind.Utc), "Not the server publication time");
        var saved = AppSettingsService.Load().OnlineWeekPublications;
        Check(saved[OnlineWeekPlanPublisher.ReceiptKey("test-company", "2030-W03")].PublishedAtUtc == receipt.PublishedAtUtc, "Receipt not persisted");
        Check(!saved.ContainsKey(OnlineWeekPlanPublisher.ReceiptKey("other-company", "2030-W03")), "Receipt crossed tenants");
        Check(!saved.ContainsKey(OnlineWeekPlanPublisher.ReceiptKey("test-company", "2030-W04")), "Receipt crossed weeks");
        Check(OnlineWeekPlanService.WeekIdFor(new DateTime(2021, 1, 1)) == "2020-W53", "ISO week year is wrong");
    }

    private static void FailedOnlinePublicationPreservesReceipt()
    {
        var settings = AppSettingsService.Load();
        settings.OnlineWeekPlanEnabled = true;
        settings.CompanyId = "test-company";
        settings.CompanyCode = "SC-TEST";
        var key = OnlineWeekPlanPublisher.ReceiptKey(settings.CompanyId, "2030-W03");
        var old = new OnlineWeekPlanPublication("2030-W03", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), "admin");
        AppSettingsService.Update(s => s.OnlineWeekPublications[key] = old);
        var snapshot = new OnlineWeekPlanSnapshot { CompanyId = settings.CompanyId, CompanyCode = settings.CompanyCode, WeekId = "2030-W03" };
        foreach (var failure in new[] { "login", "publish", "wrong-week", "missing-time" })
        {
            var calls = 0;
            using var http = new HttpClient(new PublicationHandler(_ =>
            {
                calls++;
                return Task.FromResult(calls switch
                {
                    1 when failure == "login" => JsonResponse("{}", HttpStatusCode.Unauthorized),
                    1 => JsonResponse("{\"customToken\":\"custom\"}"),
                    2 => JsonResponse("{\"idToken\":\"id\"}"),
                    _ when failure == "publish" => JsonResponse("{}", HttpStatusCode.InternalServerError),
                    _ when failure == "wrong-week" => JsonResponse("{\"ok\":true,\"weekId\":\"2030-W04\",\"publishedAtUtc\":\"2030-01-14T09:15:30Z\"}"),
                    _ => JsonResponse("{\"ok\":true,\"weekId\":\"2030-W03\"}")
                });
            }));
            var failed = false;
            try { OnlineWeekPlanPublisher.PublishAndRememberAsync(snapshot, "admin", "secret", settings, http).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed, "Invalid publication response was accepted: " + failure);
            Check(AppSettingsService.Load().OnlineWeekPublications[key] == old, "Failure replaced successful receipt");
            if (failure == "login") Check(calls == 1, "Upload attempted after rejected login");
        }
        settings.FirebasePublishEndpoint = "http://example.test/publish";
        using var noNetwork = new HttpClient(new PublicationHandler(_ => throw new Exception("Network request must not happen")));
        var rejected = false;
        try { OnlineWeekPlanPublisher.PublishAndRememberAsync(snapshot, "admin", "secret", settings, noNetwork).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "HTTP endpoint was accepted");
    }

    private static void PublicationWeekStatusAndDialog()
    {
        Planner();
        var monday = new DateTime(2030, 1, 14);
        AppSettingsService.Update(s =>
        {
            s.CompanyId = "test-company";
            s.OnlineWeekPublications[OnlineWeekPlanPublisher.ReceiptKey(s.CompanyId, "2030-W03")] =
                new OnlineWeekPlanPublication("2030-W03", new DateTime(2030, 1, 14, 9, 15, 30, DateTimeKind.Utc), "admin");
        });
        var view = new Produktionsplanung.App.Views.WeekPlanningView();
        var model = (Produktionsplanung.App.ViewModels.WeekPlanningViewModel)view.DataContext;
        model.WeekStart = monday;
        var status = (System.Windows.Controls.TextBlock)view.FindName("OnlinePublicationStatusText");
        Check(status.Text.Contains("14.01.2030") && status.Text.Contains("admin"), "Selected week receipt not shown");
        model.WeekStart = monday.AddDays(7);
        Check(status.Text.Contains("2030-W04") && !status.Text.Contains("admin"), "Another week's receipt leaked into selected week");
        Check(!((System.Windows.Controls.Button)view.FindName("PublishOnlineWeekPlanButton")).IsEnabled, "Planner can publish");
        var denied = false;
        try { OnlineWeekPlanPublisher.PublishAsync(new OnlineWeekPlanSnapshot(), "secret").GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { denied = true; }
        Check(denied, "Publisher did not enforce administrator role");
        var dialog = new Produktionsplanung.App.PublishWeekPlanWindow(new OnlineWeekPlanSnapshot { IsoWeek = 3, WeekStart = "2030-01-14", WeekEnd = "2030-01-20" });
        try
        {
            dialog.Measure(new System.Windows.Size(560, 700));
            Check(((System.Windows.Controls.PasswordBox)dialog.FindName("PasswordInput")).Password.Length == 0, "Publish dialog retained a password");
        }
        finally { dialog.Close(); }
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class PublicationHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

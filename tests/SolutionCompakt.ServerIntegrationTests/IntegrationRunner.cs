using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Produktionsplanung.App.Models;
using SolutionCompakt.Data;
using SolutionCompakt.Server.Messaging;
using SolutionCompakt.Server.Realtime;
using SolutionCompakt.Server.Security;

namespace SolutionCompakt.ServerIntegrationTests;

internal static class IntegrationRunner
{
    private static readonly Guid CompanyA = Guid.NewGuid(), CompanyB = Guid.NewGuid();
    private static string connectionString = "";
    private static int passed;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }
    private static PostgresOperationalDbContext Database(Guid company) => new(
        new DbContextOptionsBuilder<PostgresOperationalDbContext>().UseNpgsql(connectionString).Options, company);

    public static async Task<int> Main()
    {
        connectionString = Environment.GetEnvironmentVariable("SOLUTIONCOMPAKT_TEST_POSTGRES") ?? "";
        if (string.IsNullOrWhiteSpace(connectionString)) { Console.Error.WriteLine("SOLUTIONCOMPAKT_TEST_POSTGRES must point to an isolated test database."); return 1; }
        // A non-UTC server session exposes accidental timestamptz-to-timestamp casts.
        connectionString += ";Timezone=Europe/Berlin";
        Environment.SetEnvironmentVariable("ConnectionStrings__CentralDatabase", connectionString);
        Environment.SetEnvironmentVariable("Authentication__JwtSigningKey", "synthetic-integration-test-key-never-use-in-production-2026");
        Environment.SetEnvironmentVariable("License__StatusEndpoint", "https://license.invalid.test/status");
        Environment.SetEnvironmentVariable("Database__EnsureCreatedOnStartup", "false");
        try
        {
            await Seed(CompanyA); await Seed(CompanyB);
            await ConcurrencyAndSchemaIsolation();
            await CalendarAndUtcRoundtrip();
            using var factory = new TestServerFactory();
            using var anonymous = factory.CreateClient();
            Check((await anonymous.GetAsync("/api/v1/messages/inbox")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous inbox was accessible");
            Pass("API rejects anonymous access");
            using var sender = await Authenticated(factory, CompanyA, 1);
            using var recipient = await Authenticated(factory, CompanyA, 2);
            using var observer = await Authenticated(factory, CompanyA, 3);
            using var otherCompany = await Authenticated(factory, CompanyB, 2);
            var recipientEvents = new ConcurrentQueue<RealtimeEvent>();
            var observerEvents = new ConcurrentQueue<RealtimeEvent>();
            var foreignEvents = new ConcurrentQueue<RealtimeEvent>();
            var received = new TaskCompletionSource<RealtimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var recipientHub = Hub(factory, recipient, e => { recipientEvents.Enqueue(e); if (e.Type == "message.received") received.TrySetResult(e); });
            await using var observerHub = Hub(factory, observer, observerEvents.Enqueue);
            await using var foreignHub = Hub(factory, otherCompany, foreignEvents.Enqueue);
            await Task.WhenAll(recipientHub.StartAsync(), observerHub.StartAsync(), foreignHub.StartAsync());
            using var send = await sender.PostAsJsonAsync("/api/v1/messages/", new SendMessageRequest(2, "Synthetic message", "No customer data", "Wichtig"));
            send.EnsureSuccessStatusCode();
            var message = (await send.Content.ReadFromJsonAsync<MessageDto>())!;
            var push = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(push.Id == message.Id.ToString() && message.Priority == "Wichtig", "Recipient push did not match persisted message");
            Pass("HTTP send persists and pushes to another authenticated client");
            Check((await recipient.GetFromJsonAsync<MessageDto[]>("/api/v1/messages/inbox"))!.Any(x => x.Id == message.Id), "Recipient inbox omitted message");
            Check((await observer.GetFromJsonAsync<MessageDto[]>("/api/v1/messages/inbox"))!.Length == 0, "Same-company bystander read private message");
            Check((await otherCompany.GetFromJsonAsync<MessageDto[]>("/api/v1/messages/inbox"))!.Length == 0, "Another tenant read message");
            Check((await observer.PostAsync($"/api/v1/messages/{message.Id}/acknowledge", null)).StatusCode == HttpStatusCode.NotFound, "Bystander acknowledged another user's message");
            Check((await otherCompany.PostAsync($"/api/v1/messages/{message.Id}/acknowledge", null)).StatusCode == HttpStatusCode.NotFound, "Foreign tenant acknowledged message");
            Pass("Inbox and read acknowledgement are user- and tenant-scoped");
            using var acknowledgement = await recipient.PostAsync($"/api/v1/messages/{message.Id}/acknowledge", null);
            acknowledgement.EnsureSuccessStatusCode();
            var sent = (await sender.GetFromJsonAsync<MessageDto[]>("/api/v1/messages/sent"))!.Single(x => x.Id == message.Id);
            Check(sent.AcknowledgedAtUtc.HasValue, "Sender did not see read receipt");
            var acknowledgedUtc = sent.AcknowledgedAtUtc ?? throw new InvalidOperationException("Missing read timestamp");
            Check(sent.CreatedAtUtc.Kind == DateTimeKind.Utc && acknowledgedUtc.Kind == DateTimeKind.Utc &&
                Math.Abs((sent.CreatedAtUtc - DateTime.UtcNow).TotalSeconds) < 30 && Math.Abs((acknowledgedUtc - DateTime.UtcNow).TotalSeconds) < 30,
                "UTC message timestamps changed with the PostgreSQL session timezone");
            var firstRead = sent.AcknowledgedAtUtc;
            (await recipient.PostAsync($"/api/v1/messages/{message.Id}/acknowledge", null)).EnsureSuccessStatusCode();
            sent = (await sender.GetFromJsonAsync<MessageDto[]>("/api/v1/messages/sent"))!.Single(x => x.Id == message.Id);
            Check(sent.AcknowledgedAtUtc == firstRead, "Repeated acknowledgement changed original read time");
            Pass("Read receipt survives repeated acknowledgement");
            // Barrier: a subsequent company broadcast must reach the local observer after private sends.
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var barrierSubscription = observerHub.On<RealtimeEvent>("change", e => { if (e.Type == "data.changed") barrier.TrySetResult(); });
            await recipientHub.InvokeAsync("PublishChange", new[] { "Employee" });
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(observerEvents.All(e => !e.Type.StartsWith("message.")), "Private message payload was broadcast to bystander");
            Check(foreignEvents.IsEmpty, "Company events leaked to another tenant");
            Pass("SignalR events isolate private recipients and company groups");
            await using (var db = Database(CompanyA))
            {
                var audit = await db.AuditLogs.Where(x => x.EntityType == "UserMessage" && x.EntityId == message.Id.ToString()).ToListAsync();
                Check(audit.Count >= 2, "Send/read audit was not stored");
                var user = await db.UserAccounts.SingleAsync(x => x.Id == 2); user.IsActive = false; await db.SaveChangesAsync();
            }
            using var rejected = await sender.PostAsJsonAsync("/api/v1/messages/", new SendMessageRequest(2, "Disabled", "Should fail", "Normal"));
            Check(rejected.StatusCode == HttpStatusCode.BadRequest, "Disabled recipient still accepted messages");
            using var disabledLogin = await anonymous.PostAsJsonAsync("/api/v1/auth/desktop", AuthRequest(CompanyA, 2));
            Check(disabledLogin.StatusCode == HttpStatusCode.Forbidden, "Disabled user could log in");
            Pass("Writes are audited; disabled recipients and logins are rejected");
            var badRole = AuthRequest(CompanyA, 1, "Administrator");
            Check((await anonymous.PostAsJsonAsync("/api/v1/auth/desktop", badRole)).StatusCode == HttpStatusCode.Forbidden, "Client could elevate its role");
            Pass("Desktop authentication verifies persisted role");
            Console.WriteLine($"{passed} PostgreSQL/API/SignalR integration checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            await using var db = Database(CompanyA);
            foreach (var company in new[] { CompanyA, CompanyB })
            {
                var dropTestSchema = $"DROP SCHEMA IF EXISTS \"company_{company:N}\" CASCADE;";
                await db.Database.ExecuteSqlRawAsync(dropTestSchema);
            }
        }
    }

    private static async Task Seed(Guid company)
    {
        await using var db = Database(company);
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        db.UserAccounts.AddRange(Enumerable.Range(1, 3).Select(id => new UserAccount { Id = id, Username = $"synthetic-{id}", DisplayName = $"Synthetic {id}", Role = UserRoles.Planner }));
        db.Employees.Add(new Employee { PersonnelNumber = "SYNTHETIC-001", FirstName = "Synthetic", LastName = company == CompanyA ? "Company A" : "Company B" });
        await db.SaveChangesAsync();
    }

    private static async Task ConcurrencyAndSchemaIsolation()
    {
        await using var first = Database(CompanyA); await using var second = Database(CompanyA); await using var foreign = Database(CompanyB);
        var a = await first.Employees.SingleAsync(); var b = await second.Employees.SingleAsync();
        Check((await foreign.Employees.SingleAsync()).LastName == "Company B", "EF cached another tenant's schema");
        a.LastName = "First saved change"; await first.SaveChangesAsync();
        b.LastName = "Stale overwrite";
        var rejected = false; try { await second.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { rejected = true; }
        Check(rejected && (await foreign.Employees.SingleAsync()).LastName == "Company B", "Concurrent write or tenant isolation failed");
        await using var verify = Database(CompanyA);
        Check((await verify.Employees.SingleAsync()).LastName == a.LastName, "Stale client overwrote saved employee");
        Pass("Independent PostgreSQL contexts reject stale writes and isolate company schemas");
    }

    private static async Task CalendarAndUtcRoundtrip()
    {
        var calendarDate = new DateTime(2032, 4, 5, 0, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2032, 4, 4, 22, 30, 0, DateTimeKind.Utc);
        int id;
        await using (var db = Database(CompanyA))
        {
            var handover = new ShiftHandover { Subject = "Synthetic time check", Details = "No customer data", HandoverDate = calendarDate, CreatedAtUtc = utc };
            db.ShiftHandovers.Add(handover); await db.SaveChangesAsync(); id = handover.Id;
        }
        await using var verify = Database(CompanyA);
        var saved = await verify.ShiftHandovers.SingleAsync(x => x.Id == id);
        Check(saved.HandoverDate == calendarDate && saved.HandoverDate.Kind == DateTimeKind.Unspecified && saved.CreatedAtUtc == utc && saved.CreatedAtUtc.Kind == DateTimeKind.Utc,
            "Calendar dates or UTC instants changed during PostgreSQL roundtrip");
        Pass("Shared model preserves calendar dates and UTC instants without desktop timestamp switches");
    }

    private static DesktopAuthRequest AuthRequest(Guid company, int id, string role = "Planer") => new()
    {
        CompanyId = company.ToString("N"), CompanyCode = "SYNTHETIC", InstallationId = "test-installation", Secret = "test-secret", SourceUserId = id, Username = $"synthetic-{id}", DisplayName = $"Synthetic {id}", Role = role
    };
    private static async Task<HttpClient> Authenticated(TestServerFactory factory, Guid company, int id)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/desktop", AuthRequest(company, id));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<DesktopAuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    private static HubConnection Hub(TestServerFactory factory, HttpClient client, Action<RealtimeEvent> onEvent)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter;
        var hub = new HubConnectionBuilder().WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/company"), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult(token);
        }).Build();
        hub.On("change", onEvent);
        return hub;
    }

    private sealed class TestServerFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Produktionsplanung.sln"))) root = root.Parent;
            if (root is null) throw new DirectoryNotFoundException("Solution root is required for the test server.");
            builder.UseContentRoot(Path.Combine(root.FullName, "src", "SolutionCompakt.Server"));
            builder.ConfigureServices(services =>
        {
            // Only the external licensing HTTP boundary is stubbed. Authentication, database, routes and hubs are real.
            services.AddTransient(provider => new DesktopLicenseVerifier(new HttpClient(new ActiveTestLicenseHandler()), provider.GetRequiredService<IConfiguration>()));
        });
        }
    }
    private sealed class ActiveTestLicenseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"active\"}", Encoding.UTF8, "application/json") });
    }
}

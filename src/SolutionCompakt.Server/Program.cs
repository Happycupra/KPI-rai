using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SolutionCompakt.Server.Data;
using SolutionCompakt.Server.Messaging;
using SolutionCompakt.Server.Realtime;
using SolutionCompakt.Server.Security;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CentralDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:CentralDatabase is required.");

var issuer = builder.Configuration["Authentication:Issuer"] ?? "SolutionCompakt.Server";
var audience = builder.Configuration["Authentication:Audience"] ?? "SolutionCompakt.Desktop";
var signingKey = builder.Configuration["Authentication:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Authentication:JwtSigningKey must contain at least 32 UTF-8 bytes.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddDbContext<CentralDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<CentralOperationalUserVerifier>();
builder.Services.AddScoped<CentralMessageStore>();
builder.Services.AddSignalR();
builder.Services.AddHttpClient<DesktopLicenseVerifier>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<DesktopTokenIssuer>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (!Guid.TryParse(principal?.FindFirst("company_id")?.Value, out var companyId) ||
                    !int.TryParse(principal?.FindFirst("source_user_id")?.Value, out var userId))
                {
                    context.Fail("Ungültiger Firmen- oder Benutzerkontext.");
                    return;
                }
                var verifier = context.HttpContext.RequestServices.GetRequiredService<CentralOperationalUserVerifier>();
                var user = await verifier.VerifyAsync(companyId, userId,
                    principal?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? string.Empty,
                    principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty,
                    context.HttpContext.RequestAborted);
                if (!user.Allowed) context.Fail(user.Message);
            },
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs/company"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:EnsureCreatedOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CentralDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (CentralDbContext db, CancellationToken cancellationToken) =>
{
    var databaseAvailable = await db.Database.CanConnectAsync(cancellationToken);
    return databaseAvailable
        ? Results.Ok(new { status = "ok", database = "available", utc = DateTime.UtcNow })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapPost("/api/v1/auth/desktop", async (
    DesktopAuthRequest request,
    DesktopLicenseVerifier licenseVerifier,
    CentralOperationalUserVerifier userVerifier,
    DesktopTokenIssuer tokenIssuer,
    CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(request.CompanyId, out var companyId) || companyId == Guid.Empty)
        return Results.BadRequest(new { error = "Ungültige Firmen-ID." });
    if (!Regex.IsMatch(request.CompanyCode?.Trim() ?? string.Empty, "^[A-Za-z0-9-]{3,24}$"))
        return Results.BadRequest(new { error = "Ungültiger Firmen-Code." });
    if (request.SourceUserId < 1 || string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100)
        return Results.BadRequest(new { error = "Ungültiger Benutzer." });
    if (request.Role is not ("Administrator" or "Planer" or "Beobachter"))
        return Results.BadRequest(new { error = "Ungültige Benutzerrolle." });

    var license = await licenseVerifier.VerifyAsync(
        request.InstallationId,
        request.Secret,
        request.AppVersion,
        cancellationToken);
    if (!license.Allowed)
        return Results.Json(new { error = license.Message }, statusCode: StatusCodes.Status403Forbidden);

    var user = await userVerifier.VerifyAsync(
        companyId,
        request.SourceUserId,
        request.Username,
        request.Role,
        cancellationToken);
    if (!user.Allowed)
        return Results.Json(new { error = user.Message }, statusCode: StatusCodes.Status403Forbidden);

    return Results.Ok(tokenIssuer.Issue(request, companyId));
}).AllowAnonymous();

app.MapGet("/api/v1/me", (ITenantContext tenant) => Results.Ok(new
{
    companyId = tenant.RequireCompanyId().ToString("N"),
    sourceUserId = tenant.RequireSourceUserId(),
    role = tenant.Role
})).RequireAuthorization();

var messages = app.MapGroup("/api/v1/messages").RequireAuthorization();

messages.MapGet("/recipients", async (
    ITenantContext tenant,
    CentralMessageStore store,
    CancellationToken cancellationToken) =>
{
    var result = await store.GetRecipientsAsync(
        tenant.RequireCompanyId(),
        tenant.RequireSourceUserId(),
        cancellationToken);
    return Results.Ok(result);
});

messages.MapGet("/inbox", async (
    ITenantContext tenant,
    CentralMessageStore store,
    CancellationToken cancellationToken) => Results.Ok(await store.GetInboxAsync(
        tenant.RequireCompanyId(),
        tenant.RequireSourceUserId(),
        cancellationToken)));

messages.MapGet("/sent", async (
    ITenantContext tenant,
    CentralMessageStore store,
    CancellationToken cancellationToken) => Results.Ok(await store.GetSentAsync(
        tenant.RequireCompanyId(),
        tenant.RequireSourceUserId(),
        cancellationToken)));

messages.MapGet("/unread", async (
    ITenantContext tenant,
    CentralMessageStore store,
    CancellationToken cancellationToken) => Results.Ok(await store.GetUnreadAsync(
        tenant.RequireCompanyId(),
        tenant.RequireSourceUserId(),
        cancellationToken)));

messages.MapGet("/unread-count", async (
    ITenantContext tenant,
    CentralMessageStore store,
    CancellationToken cancellationToken) => Results.Ok(new MessageCountDto(await store.GetUnreadCountAsync(
        tenant.RequireCompanyId(),
        tenant.RequireSourceUserId(),
        cancellationToken))));

messages.MapPost("/", async (
    SendMessageRequest request,
    ITenantContext tenant,
    CentralMessageStore store,
    IHubContext<CompanyHub> hub,
    CancellationToken cancellationToken) =>
{
    try
    {
        var companyId = tenant.RequireCompanyId();
        var senderUserId = tenant.RequireSourceUserId();
        var message = await store.SendAsync(companyId, senderUserId, request, cancellationToken);

        var received = new RealtimeEvent(
            "message.received",
            message.Id.ToString(),
            message.CreatedAtUtc,
            senderUserId,
            new[] { "UserMessage" });
        var sent = new RealtimeEvent(
            "message.sent",
            message.Id.ToString(),
            message.CreatedAtUtc,
            senderUserId,
            new[] { "UserMessage" });

        await Task.WhenAll(
            hub.Clients.Group(CompanyHub.UserGroupName(companyId, message.RecipientUserId))
                .SendAsync("change", received, cancellationToken),
            hub.Clients.Group(CompanyHub.UserGroupName(companyId, senderUserId))
                .SendAsync("change", sent, cancellationToken));

        return Results.Ok(message);
    }
    catch (MessageValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

messages.MapPost("/{messageId:int}/acknowledge", async (
    int messageId,
    ITenantContext tenant,
    CentralMessageStore store,
    IHubContext<CompanyHub> hub,
    CancellationToken cancellationToken) =>
{
    try
    {
        var companyId = tenant.RequireCompanyId();
        var recipientUserId = tenant.RequireSourceUserId();
        var result = await store.AcknowledgeAsync(companyId, recipientUserId, cancellationToken, messageId);
        if (!result.Found)
            return Results.NotFound(new { error = "Hinweis wurde nicht gefunden." });

        var realtime = new RealtimeEvent(
            "message.acknowledged",
            messageId.ToString(),
            result.AcknowledgedAtUtc ?? DateTime.UtcNow,
            recipientUserId,
            new[] { "UserMessage" });

        await Task.WhenAll(
            hub.Clients.Group(CompanyHub.UserGroupName(companyId, result.SenderUserId))
                .SendAsync("change", realtime, cancellationToken),
            hub.Clients.Group(CompanyHub.UserGroupName(companyId, recipientUserId))
                .SendAsync("change", realtime, cancellationToken));

        return Results.Ok(result);
    }
    catch (MessageValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/v1/realtime/ping", async (
    ITenantContext tenant,
    IHubContext<CompanyHub> hub,
    CancellationToken cancellationToken) =>
{
    var companyId = tenant.RequireCompanyId();
    var payload = new RealtimeEvent(
        "server.ping",
        Guid.NewGuid().ToString("N"),
        DateTime.UtcNow,
        tenant.RequireSourceUserId(),
        Array.Empty<string>());
    await hub.Clients.Group(CompanyHub.GroupName(companyId))
        .SendAsync("change", payload, cancellationToken);
    return Results.Accepted(value: payload);
}).RequireAuthorization();

app.MapHub<CompanyHub>("/hubs/company", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();

app.Run();

public partial class Program;

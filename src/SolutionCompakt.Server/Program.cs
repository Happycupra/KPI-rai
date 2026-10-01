using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SolutionCompakt.Server.Data;
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

app.MapHub<CompanyHub>("/hubs/company").RequireAuthorization();

app.Run();

public partial class Program;

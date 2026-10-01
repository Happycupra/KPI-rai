using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace SolutionCompakt.Server.Security;

public sealed class DesktopAuthRequest
{
    public string CompanyId { get; init; } = string.Empty;
    public string CompanyCode { get; init; } = string.Empty;
    public string InstallationId { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public int SourceUserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
}

public sealed record DesktopAuthResponse(string AccessToken, DateTime ExpiresAtUtc);
public sealed record DesktopLicenseValidation(bool Allowed, string Message);

public sealed class DesktopLicenseVerifier(HttpClient http, IConfiguration configuration)
{
    public async Task<DesktopLicenseValidation> VerifyAsync(
        string installationId,
        string secret,
        string appVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installationId) || string.IsNullOrWhiteSpace(secret))
            return new(false, "Installation ist nicht vollständig registriert.");

        var endpoint = configuration["License:StatusEndpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new(false, "Lizenzprüfung ist serverseitig nicht gültig konfiguriert.");

        try
        {
            using var response = await http.PostAsJsonAsync(uri, new
            {
                installationId,
                secret,
                appVersion = string.IsNullOrWhiteSpace(appVersion) ? "central-server" : appVersion
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new(false, "Lizenz konnte nicht bestätigt werden.");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString() ?? string.Empty
                : string.Empty;
            var allowed = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
            return allowed
                ? new(true, "Lizenz aktiv.")
                : new(false, "Zentraler Mehrbenutzerbetrieb erfordert eine aktive Lizenz.");
        }
        catch (Exception ex)
        {
            return new(false, "Lizenzprüfung nicht erreichbar: " + ex.Message);
        }
    }
}

public sealed class DesktopTokenIssuer(IConfiguration configuration)
{
    public DesktopAuthResponse Issue(DesktopAuthRequest request, Guid companyId)
    {
        var issuer = configuration["Authentication:Issuer"] ?? "SolutionCompakt.Server";
        var audience = configuration["Authentication:Audience"] ?? "SolutionCompakt.Desktop";
        var signingKey = configuration["Authentication:JwtSigningKey"]
            ?? throw new InvalidOperationException("Authentication:JwtSigningKey is missing.");

        var now = DateTime.UtcNow;
        var expires = now.AddHours(8);
        var claims = new List<Claim>
        {
            new("company_id", companyId.ToString("N")),
            new("company_code", request.CompanyCode.Trim().ToUpperInvariant()),
            new("source_user_id", request.SourceUserId.ToString()),
            new(ClaimTypes.Name, request.Username.Trim()),
            new("display_name", request.DisplayName.Trim()),
            new(ClaimTypes.Role, request.Role.Trim())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: now.AddMinutes(-1),
            expires: expires,
            signingCredentials: credentials);
        return new DesktopAuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

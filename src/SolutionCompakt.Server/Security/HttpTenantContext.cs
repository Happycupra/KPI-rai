using System.Security.Claims;

namespace SolutionCompakt.Server.Security;

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? CompanyIdOrNull
    {
        get
        {
            var raw = User?.FindFirstValue("company_id") ?? User?.FindFirstValue("companyId");
            return Guid.TryParse(raw, out var companyId) ? companyId : null;
        }
    }

    public int? SourceUserIdOrNull
    {
        get
        {
            var raw = User?.FindFirstValue("source_user_id") ?? User?.FindFirstValue("sourceUserId");
            return int.TryParse(raw, out var sourceUserId) && sourceUserId > 0 ? sourceUserId : null;
        }
    }

    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? User?.FindFirstValue("role") ?? string.Empty;

    public Guid RequireCompanyId() => CompanyIdOrNull
        ?? throw new UnauthorizedAccessException("The authenticated token does not contain a valid company_id claim.");

    public int RequireSourceUserId() => SourceUserIdOrNull
        ?? throw new UnauthorizedAccessException("The authenticated token does not contain a valid source_user_id claim.");
}

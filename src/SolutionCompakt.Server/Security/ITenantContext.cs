namespace SolutionCompakt.Server.Security;

public interface ITenantContext
{
    Guid? CompanyIdOrNull { get; }
    int? SourceUserIdOrNull { get; }
    string Role { get; }
    Guid RequireCompanyId();
    int RequireSourceUserId();
}

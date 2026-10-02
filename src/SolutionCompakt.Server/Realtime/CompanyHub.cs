using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SolutionCompakt.Server.Realtime;

[Authorize]
public sealed class CompanyHub : Hub
{
    public static string GroupName(Guid companyId) => "company:" + companyId.ToString("N");
    public static string UserGroupName(Guid companyId, int sourceUserId) =>
        $"company:{companyId:N}:user:{sourceUserId}";

    public override async Task OnConnectedAsync()
    {
        if (!TryGetCompanyId(out var companyId) || !TryGetSourceUserId(out var sourceUserId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(companyId));
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(companyId, sourceUserId));
        await base.OnConnectedAsync();
    }

    public async Task PublishChange(string[]? entityTypes)
    {
        if (!TryGetCompanyId(out var companyId))
            throw new HubException("Ungültige Firmenzuordnung.");
        if (!TryGetSourceUserId(out var sourceUserId))
            throw new HubException("Ungültiger Benutzerkontext.");

        var safeTypes = (entityTypes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => x.Length <= 120)
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();

        var payload = new RealtimeEvent(
            "data.changed",
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            sourceUserId,
            safeTypes);

        await Clients.OthersInGroup(GroupName(companyId)).SendAsync("change", payload);
    }

    private bool TryGetCompanyId(out Guid companyId)
    {
        var raw = Context.User?.FindFirst("company_id")?.Value ?? Context.User?.FindFirst("companyId")?.Value;
        return Guid.TryParse(raw, out companyId);
    }

    private bool TryGetSourceUserId(out int sourceUserId)
    {
        var raw = Context.User?.FindFirst("source_user_id")?.Value ?? Context.User?.FindFirst("sourceUserId")?.Value;
        return int.TryParse(raw, out sourceUserId) && sourceUserId > 0;
    }
}

public sealed record RealtimeEvent(
    string Type,
    string Id,
    DateTime OccurredAtUtc,
    int SourceUserId,
    IReadOnlyList<string>? EntityTypes = null);

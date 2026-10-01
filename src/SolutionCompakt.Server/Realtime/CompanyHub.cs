using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SolutionCompakt.Server.Realtime;

[Authorize]
public sealed class CompanyHub : Hub
{
    public static string GroupName(Guid companyId) => "company:" + companyId.ToString("N");

    public override async Task OnConnectedAsync()
    {
        if (!TryGetCompanyId(out var companyId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(companyId));
        await base.OnConnectedAsync();
    }

    public async Task PublishChange(string[]? entityTypes)
    {
        if (!TryGetCompanyId(out var companyId))
            throw new HubException("Ungültige Firmenzuordnung.");

        var sourceRaw = Context.User?.FindFirst("source_user_id")?.Value ?? Context.User?.FindFirst("sourceUserId")?.Value;
        if (!int.TryParse(sourceRaw, out var sourceUserId) || sourceUserId < 1)
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
}

public sealed record RealtimeEvent(
    string Type,
    string Id,
    DateTime OccurredAtUtc,
    int SourceUserId,
    IReadOnlyList<string>? EntityTypes = null);

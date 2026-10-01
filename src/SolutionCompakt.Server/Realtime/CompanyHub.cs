using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SolutionCompakt.Server.Realtime;

[Authorize]
public sealed class CompanyHub : Hub
{
    public static string GroupName(Guid companyId) => "company:" + companyId.ToString("N");

    public override async Task OnConnectedAsync()
    {
        var raw = Context.User?.FindFirst("company_id")?.Value ?? Context.User?.FindFirst("companyId")?.Value;
        if (!Guid.TryParse(raw, out var companyId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(companyId));
        await base.OnConnectedAsync();
    }
}

public sealed record RealtimeEvent(string Type, string Id, DateTime OccurredAtUtc, int SourceUserId);

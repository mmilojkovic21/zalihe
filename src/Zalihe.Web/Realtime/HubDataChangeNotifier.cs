using Microsoft.AspNetCore.SignalR;
using Zalihe.Application.Common;

namespace Zalihe.Web.Realtime;

/// <summary>Sends <see cref="ChangesHub.DataChanged"/> to the open screens of each company, from requests and background jobs alike.</summary>
public class HubDataChangeNotifier(IHubContext<ChangesHub> hub, IHttpContextAccessor httpContextAccessor, ILogger<HubDataChangeNotifier> logger)
    : IDataChangeNotifier
{
    // SignalR's connection ids are 22 characters; anything longer isn't one.
    private const int ConnectionIdMaxLength = 64;

    public async Task DataChangedAsync(IReadOnlyCollection<Guid> tenantIds)
    {
        try
        {
            // The tab that made the change (none in background jobs). The header comes from the client, but all it
            // can do is skip a notice: connection ids are random and never shown to other users.
            var source = httpContextAccessor.HttpContext?.Request.Headers[ChangesHub.ConnectionHeader].ToString();
            IReadOnlyList<string> except = string.IsNullOrEmpty(source) || source.Length > ConnectionIdMaxLength ? [] : [source];

            foreach (var tenantId in tenantIds)
            {
                await hub.Clients.GroupExcept(ChangesHub.GroupOf(tenantId), except).SendAsync(ChangesHub.DataChanged);
            }
        }
        catch (Exception ex)
        {
            // The change is already saved; screens catch up on their next refresh or reconnect.
            logger.LogWarning(ex, "Could not announce a data change to {TenantCount} companies.", tenantIds.Count);
        }
    }
}

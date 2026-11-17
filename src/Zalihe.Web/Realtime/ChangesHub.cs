using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Zalihe.Infrastructure.Identity;

namespace Zalihe.Web.Realtime;

/// <summary>
/// Tells open screens that their company's data changed (SPEC 11.1). Clients only listen; the one message,
/// <see cref="DataChanged"/>, carries no data, so every user reloads through the API and sees what their role allows.
/// </summary>
[Authorize]
public class ChangesHub : Hub
{
    public const string Path = "/hubs/changes";

    /// <summary>The client message: reload the company's data.</summary>
    public const string DataChanged = "dataChanged";

    /// <summary>
    /// Sent by the web app with every change it makes: its own connection. That tab already reloads what it changed,
    /// so the notice skips it; the user's other tabs and everyone else in the company still get it.
    /// </summary>
    public const string ConnectionHeader = "X-Connection-Id";

    public static string GroupOf(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        // The company comes from the sign-in, never from the client, so nobody can join another company's group.
        if (!Guid.TryParse(Context.User?.FindFirst(AppClaims.TenantId)?.Value, out var tenantId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(tenantId));
        await base.OnConnectedAsync();
    }
}

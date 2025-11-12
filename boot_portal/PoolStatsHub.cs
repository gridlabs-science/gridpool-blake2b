using boot_portal.HostedServices;
using Microsoft.AspNetCore.SignalR;

namespace boot_portal;

public class PoolStatsHub : Hub
{
    // Clients can call this to get the current state when they load
    public async Task GetInitialData()
    {
        // Send the current data *only to the caller*
        await Clients.Caller.SendAsync("UpdateWinners", DatumServer.WinnersList);
        await Clients.Caller.SendAsync("UpdateOnDeck", DatumServer.OnDeckList);
    }
}
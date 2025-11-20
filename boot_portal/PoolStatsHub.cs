namespace boot_portal;

using Microsoft.AspNetCore.SignalR;
using boot_portal.HostedServices; // Namespace where DatumServer lives

public class PoolStatsHub : Hub
{
    public async Task GetInitialData()
    {
        // Send Lists
        await Clients.Caller.SendAsync("UpdateWinners", DatumServer.WinnersList);
        await Clients.Caller.SendAsync("UpdateOnDeck", DatumServer.OnDeckList);
        
        // Send Best Record
        await Clients.Caller.SendAsync("UpdateRecord", DatumServer.BestShare);
        
        // Send Server Config Info (Public Key)
        await Clients.Caller.SendAsync("UpdateServerInfo", new { 
            pubKey = DatumServer.ServerPubKeyHex,
            poolPort = 3008 // You might want to make this dynamic too via Program config
        });
    }
}
namespace boot_portal;

using Microsoft.AspNetCore.SignalR;
using boot_portal.HostedServices; // Namespace where DatumServer lives
using boot_portal.Services;

public class PoolStatsHub : Hub
{
    private readonly BootProtocolStateService _stateService;
    private readonly PoolConfig _poolConfig;

    public PoolStatsHub(BootProtocolStateService stateService, PoolConfig poolConfig)
    {
        _stateService = stateService;
        _poolConfig = poolConfig;
    }

    public async Task GetInitialData()
    {
        // Send Lists
        await Clients.Caller.SendAsync("UpdateWinners", _stateService.GetWinnersList());
        await Clients.Caller.SendAsync("UpdateOnDeck", _stateService.GetOnDeckList());
        
        // Send Best Record
        await Clients.Caller.SendAsync("UpdateRecord", _stateService.GetBestShare());
        await Clients.Caller.SendAsync("UpdateNetworkState", _stateService.GetNetworkStatus());
        
        // Send Server Config Info (Public Key)
        await Clients.Caller.SendAsync("UpdateServerInfo", new { 
            pubKey = DatumServer.ServerPubKeyHex,
            poolPort = DatumServer.PoolPort,
            datumHost = ResolveDatumHost()
        });
    }

    private string ResolveDatumHost()
    {
        if (!string.IsNullOrWhiteSpace(_poolConfig.DatumPublicHost))
        {
            string configured = _poolConfig.DatumPublicHost.Trim().TrimEnd('/');
            if (Uri.TryCreate(configured, UriKind.Absolute, out Uri? hostUri))
            {
                return hostUri.IsDefaultPort ? hostUri.Host : hostUri.Authority;
            }

            return configured;
        }

        if (Uri.TryCreate(_poolConfig.PublicBaseUrl, UriKind.Absolute, out Uri? publicUri))
        {
            return publicUri.IsDefaultPort ? publicUri.Host : publicUri.Authority;
        }

        return "--";
    }
}

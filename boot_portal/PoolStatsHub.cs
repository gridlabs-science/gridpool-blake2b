namespace boot_portal;

using Microsoft.AspNetCore.SignalR;
using boot_portal.HostedServices; // Namespace where DatumServer lives
using boot_portal.Services;
using boot_portal.Models;
using boot_portal.Utils;

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
        await Clients.Caller.SendAsync("UpdateRoundHistory", _stateService.GetRoundHistory());
        
        // Send Best Record
        await Clients.Caller.SendAsync("UpdateRecord", _stateService.GetBestShare());
        await Clients.Caller.SendAsync("UpdateNetworkState", _stateService.GetPublicNetworkStatus());
        
        // Send Server Config Info (Public Key)
        await Clients.Caller.SendAsync("UpdateServerInfo", new { 
            pubKey = DatumServer.ServerPubKeyHex,
            poolPort = ResolveDatumPort(),
            datumHost = ResolveDatumHost()
        });
    }

    private string ResolveDatumHost()
    {
        if (!string.IsNullOrWhiteSpace(_poolConfig.DatumPublicHost))
        {
            string configured = _poolConfig.DatumPublicHost.Trim().TrimEnd('/');
            bool hasScheme = configured.Contains("://", StringComparison.Ordinal);
            bool parsed = Uri.TryCreate(
                hasScheme ? configured : $"tcp://{configured}",
                UriKind.Absolute,
                out Uri? hostUri);
            if (parsed && hostUri != null)
            {
                string publicHost = BootPrivacy.KeepPublicDnsHost(hostUri.Host);
                return string.IsNullOrWhiteSpace(publicHost)
                    ? "--"
                    : hostUri.IsDefaultPort
                        ? publicHost
                        : $"{publicHost}:{hostUri.Port}";
            }
        }

        if (Uri.TryCreate(_poolConfig.PublicBaseUrl, UriKind.Absolute, out Uri? publicUri))
        {
            string publicHost = BootPrivacy.KeepPublicDnsHost(publicUri.Host);
            return string.IsNullOrWhiteSpace(publicHost)
                ? "--"
                : publicUri.IsDefaultPort
                    ? publicHost
                    : $"{publicHost}:{publicUri.Port}";
        }

        return "--";
    }

    private int ResolveDatumPort()
    {
        return _poolConfig.DatumPublicPort > 0
            ? _poolConfig.DatumPublicPort
            : DatumServer.PoolPort;
    }
}

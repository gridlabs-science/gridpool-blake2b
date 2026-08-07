using boot_portal.Services;
using Microsoft.AspNetCore.SignalR;

namespace boot_portal;

public sealed class DashboardHub : Hub
{
    private readonly DashboardRevisionService _revisionService;

    public DashboardHub(DashboardRevisionService revisionService)
    {
        _revisionService = revisionService;
    }

    public Task<long> GetRevision() => Task.FromResult(_revisionService.CurrentRevision);
}

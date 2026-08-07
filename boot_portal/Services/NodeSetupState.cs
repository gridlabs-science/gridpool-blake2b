namespace boot_portal.Services;

public sealed class NodeSetupState
{
    private readonly object _gate = new();
    private string _pendingPayoutAddress = string.Empty;

    public NodeSetupState(bool operationalAtStartup)
    {
        OperationalAtStartup = operationalAtStartup;
    }

    public bool OperationalAtStartup { get; }

    public bool RestartRequired
    {
        get
        {
            lock (_gate)
            {
                return !string.IsNullOrWhiteSpace(_pendingPayoutAddress);
            }
        }
    }

    public string PendingPayoutAddress
    {
        get
        {
            lock (_gate)
            {
                return _pendingPayoutAddress;
            }
        }
    }

    public void MarkSaved(string payoutAddress)
    {
        lock (_gate)
        {
            _pendingPayoutAddress = payoutAddress;
        }
    }
}

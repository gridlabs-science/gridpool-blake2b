using boot_portal.Models;
using boot_portal.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace boot_portal.Pages;

public sealed class IndexModel : PageModel
{
    private readonly PoolConfig _poolConfig;

    public IndexModel(PoolConfig poolConfig)
    {
        _poolConfig = poolConfig;
    }

    public IActionResult OnGet()
    {
        if (!IsSetupComplete())
        {
            return RedirectToPage("/Setup");
        }

        return Page();
    }

    private bool IsSetupComplete()
    {
        if (_poolConfig.SetupCompleted)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_poolConfig.PoolPayoutScript) &&
               BitcoinScript.TryAddressToScriptPubKey(_poolConfig.PoolPayoutScript, _poolConfig.BitcoinNetwork, out _);
    }
}

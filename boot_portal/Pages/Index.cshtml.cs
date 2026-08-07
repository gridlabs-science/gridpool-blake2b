using boot_portal.Models;
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
        if (!_poolConfig.IsSetupComplete())
        {
            return RedirectToPage("/Setup");
        }

        return Page();
    }
}

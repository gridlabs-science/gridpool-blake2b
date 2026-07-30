using System.ComponentModel.DataAnnotations;
using boot_portal.Models;
using boot_portal.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace boot_portal.Pages;

public sealed class SetupModel(PoolConfig poolConfig) : PageModel
{
    private readonly PoolConfig _poolConfig = poolConfig;

    [BindProperty]
    [Required(ErrorMessage = "Bitcoin payout address is required.")]
    public string PoolPayoutScript { get; set; } = string.Empty;

    public string? SavedAddress { get; private set; }

    public IActionResult OnGet()
    {
        SavedAddress = _poolConfig.PoolPayoutScript;
        if (_poolConfig.SetupCompleted && !string.IsNullOrWhiteSpace(_poolConfig.PoolPayoutScript))
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var bitcoinNetwork = BitcoinScript.NormalizeNetwork(_poolConfig.BitcoinNetwork);
        if (!BitcoinScript.TryAddressToScriptPubKey(PoolPayoutScript.Trim(), bitcoinNetwork, out _))
        {
            ModelState.AddModelError(nameof(PoolPayoutScript), $"Bitcoin payout address is not valid for bitcoin network {bitcoinNetwork}.");
            return Page();
        }

        _poolConfig.PoolPayoutScript = PoolPayoutScript.Trim();
        _poolConfig.SetupCompleted = true;

        try
        {
            Program.SaveSetupConfig(_poolConfig);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, "Failed to save setup configuration: " + ex.Message);
            return Page();
        }

        return RedirectToPage("/Index");
    }
}

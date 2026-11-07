using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Settings;

/// <summary>The user's settings: where reminders are emailed. Their settings row is added on first save.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public SettingsInput Settings { get; set; } = new();

    public async Task OnGetAsync()
    {
        Settings = await db.UserSettings.Select(SettingsInput.Projection).FirstOrDefaultAsync() ?? new();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var existing = await db.UserSettings.FirstOrDefaultAsync();
        if (existing is null)
        {
            existing = new UserSettings();
            Settings.ApplyTo(existing);
            db.UserSettings.Add(existing);
            await db.SaveChangesAsync();
        }
        else
        {
            Settings.ApplyTo(existing);
            if (!await db.TrySaveEditAsync(existing, Settings.RowVersion))
            {
                ModelState.AddModelError(string.Empty,
                    "Your settings changed after you opened them (perhaps in another tab), so your changes haven't been saved. " +
                    "Save again to keep yours.");
                Settings.RowVersion = existing.RowVersion;
                ModelState.Remove("Settings.RowVersion");
                return Page();
            }
        }

        TempData["Message"] = "Settings saved.";
        return RedirectToPage();
    }
}

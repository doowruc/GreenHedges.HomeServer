using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Resend;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Settings;

/// <summary>The user's settings: where reminders and scheduled backups are emailed. Their settings row is added on first save.</summary>
public class IndexModel(AppDbContext db, BackupEmailer emailer, ILogger<IndexModel> logger) : PageModel
{
    private static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [BindProperty]
    public SettingsInput Settings { get; set; } = new();

    /// <summary>The last scheduled backup (UK time).</summary>
    public DateTime? LastScheduledBackup { get; private set; }

    public async Task OnGetAsync()
    {
        Settings = await db.UserSettings.Select(SettingsInput.Projection).FirstOrDefaultAsync() ?? new();
        await LoadLastScheduledBackupAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadLastScheduledBackupAsync();
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
                    "Your settings changed after you opened them (perhaps a scheduled backup was sent), so your changes haven't been saved. " +
                    "Save again to keep yours.");
                Settings.RowVersion = existing.RowVersion;
                ModelState.Remove("Settings.RowVersion");
                return Page();
            }
        }

        TempData["Message"] = "Settings saved.";
        return RedirectToPage();
    }

    /// <summary>Emails a backup to the saved address now, whether or not anything has changed. It doesn't affect the schedule.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        ModelState.Clear();   // the settings form's values aren't posted with this one
        await OnGetAsync();
        try
        {
            if (await emailer.EmailAsync(scheduled: false) == BackupEmailResult.NoAddress)
            {
                ModelState.AddModelError(string.Empty, "Save a backup email address first.");
                return Page();
            }
        }
        catch (ResendException ex)
        {
            logger.LogWarning(ex, "Failed to email a backup");
            ModelState.AddModelError(string.Empty, $"The backup couldn't be emailed: {ex.Message}");
            return Page();
        }

        TempData["Message"] = $"Backup emailed to {Settings.BackupEmail}.";
        return RedirectToPage();
    }

    private async Task LoadLastScheduledBackupAsync()
    {
        var last = await db.UserSettings.Select(s => s.LastBackupDate).FirstOrDefaultAsync();
        LastScheduledBackup = last is { } utc ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), UkTime) : null;
    }
}

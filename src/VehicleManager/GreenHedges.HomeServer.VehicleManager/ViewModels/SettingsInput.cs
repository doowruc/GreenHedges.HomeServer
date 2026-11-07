using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class SettingsInput
{
    [EmailAddress, StringLength(254), Display(Name = "Reminder email")]
    public string? ReminderEmail { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<UserSettings, SettingsInput>> Projection = s => new SettingsInput
    {
        ReminderEmail = s.ReminderEmail, RowVersion = s.RowVersion
    };

    public void ApplyTo(UserSettings settings)
    {
        settings.ReminderEmail = Tidy(ReminderEmail);
    }

    private static string? Tidy(string? address) => string.IsNullOrWhiteSpace(address) ? null : address.Trim();
}

using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A user's settings (where reminders go). At most one row per user, added the first time it's needed. Not part of a
/// backup: restoring doesn't change where reminders go.
/// </summary>
public class UserSettings : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Where reminders of MOT, tax, insurance and service due dates are emailed; none means no reminders.</summary>
    [StringLength(254)]
    public string? ReminderEmail { get; set; }
}

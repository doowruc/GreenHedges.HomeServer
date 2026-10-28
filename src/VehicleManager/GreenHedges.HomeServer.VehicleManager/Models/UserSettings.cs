using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A user's settings (where backups and reminders go), and their last scheduled backup. At most one row per user, added the
/// first time it's needed. Not part of a backup: restoring doesn't change where backups go.
/// </summary>
public class UserSettings : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Where the nightly backup is emailed; none means no scheduled backups.</summary>
    [StringLength(254)]
    public string? BackupEmail { get; set; }

    /// <summary>Where reminders of MOT, tax, insurance and service due dates are emailed; none means no reminders.</summary>
    [StringLength(254)]
    public string? ReminderEmail { get; set; }

    /// <summary>When the last scheduled backup was emailed (UTC). Downloads and "Email a backup now" don't count.</summary>
    public DateTime? LastBackupDate { get; set; }

    /// <summary>The last scheduled backup's <c>BackupFile.ContentHash</c>, to tell whether the data has changed since.</summary>
    [StringLength(64)]
    public string? LastBackupHash { get; set; }
}

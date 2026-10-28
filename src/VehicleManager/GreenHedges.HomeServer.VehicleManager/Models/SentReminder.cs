using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A reminder email already sent: <see cref="DaysBefore"/> days before <see cref="DueDate"/>, for one of a
/// vehicle's due dates. It stops the same reminder being sent twice; a changed due date gets reminders of its own.
/// A service due by mileage is identified by <see cref="DueMileage"/> instead, as its estimated date moves with each fill-up.
/// Bookkeeping only, so not part of a backup. Belongs to the user who owns the vehicle, and goes with it.
/// </summary>
public class SentReminder : AuditedEntity
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DueDateKind Kind { get; set; }
    /// <summary>The due date, or for a service due by mileage, its estimated date when the reminder was sent.</summary>
    public DateOnly DueDate { get; set; }
    /// <summary>For a service due by mileage, the mileage it's due at.</summary>
    public int? DueMileage { get; set; }
    public int DaysBefore { get; set; }
}

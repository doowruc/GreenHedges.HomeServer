using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// One of a vehicle's MOT tests, as the DVSA's MOT history has it. Kept in step with the DVSA by each lookup
/// (<c>Services/MotTests</c>) rather than typed in. Pages use <c>MotTestView</c> rather than this entity.
/// </summary>
public class MotTest : AuditedEntity
{
    public const int MaxTestNumberLength = 20, MaxDataSourceLength = 10;

    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>When the test was completed (UTC).</summary>
    public DateTime CompletedDate { get; set; }

    public bool Passed { get; set; }

    /// <summary>When the certificate runs out (passes only).</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>The odometer reading, if it was read; in kilometres if <see cref="OdometerInKilometres"/>.</summary>
    public int? Odometer { get; set; }
    public bool OdometerInKilometres { get; set; }

    [StringLength(MaxTestNumberLength)]
    public string? MotTestNumber { get; set; }

    /// <summary>The registration when tested, which differs after a change of number plate.</summary>
    [StringLength(15)]
    public string? RegistrationAtTest { get; set; }

    /// <summary>"DVSA" (Great Britain), "DVA NI" (Northern Ireland, which has no defect details) or "CVS".</summary>
    [StringLength(MaxDataSourceLength)]
    public string? DataSource { get; set; }

    public List<MotDefect> Defects { get; set; } = [];
}

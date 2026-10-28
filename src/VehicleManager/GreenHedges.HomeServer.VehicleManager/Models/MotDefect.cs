using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A defect or advisory noted at an MOT test, in the DVSA's order (<see cref="Sequence"/>).</summary>
public class MotDefect : AuditedEntity
{
    public const int MaxTypeLength = 20, MaxTextLength = 2000;

    public int Id { get; set; }

    public int MotTestId { get; set; }
    public MotTest? MotTest { get; set; }

    public int Sequence { get; set; }

    /// <summary>The DVSA's type: ADVISORY, MINOR, MAJOR, DANGEROUS, FAIL, PRS, USER ENTERED and so on.</summary>
    [StringLength(MaxTypeLength)]
    public string? Type { get; set; }

    [Required, StringLength(MaxTextLength)]
    public string Text { get; set; } = string.Empty;

    public bool Dangerous { get; set; }
}

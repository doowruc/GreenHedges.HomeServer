using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A stored owner. Pages use <c>OwnerInput</c> or their own projections rather than this entity.</summary>
public class Owner : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    // --- Their driving licence (optional): when it was first issued (for how long they've held it, e.g. for insurance
    //     quotes) and the driver number, 16 characters with no spaces (see OwnerInput.DriverNumberPattern). ---
    public DateOnly? LicenceIssueDate { get; set; }

    [StringLength(MaxDriverNumberLength)]
    public string? DriverNumber { get; set; }

    public const int MaxDriverNumberLength = 16;

    public List<Vehicle> Vehicles { get; set; } = [];
    public List<InsurancePolicy> InsurancePolicies { get; set; } = [];
    public List<Claim> Claims { get; set; } = [];
}

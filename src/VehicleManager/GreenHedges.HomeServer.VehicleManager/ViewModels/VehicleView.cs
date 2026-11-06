using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class VehicleView
{
    public int Id { get; init; }
    public VehicleType Type { get; init; }
    public FuelType FuelType { get; init; }
    public string Make { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public DateOnly? RegistrationDate { get; init; }
    public DateOnly? PurchasedDate { get; init; }
    public decimal? PurchasePrice { get; init; }
    public DisposalMethod? DisposalMethod { get; init; }
    public DateOnly? DisposedDate { get; init; }
    public decimal? DisposalPrice { get; init; }
    public DateOnly? MotDueDate { get; init; }
    public DateOnly? TaxDueDate { get; init; }
    /// <summary>When the latest insurance policy the vehicle is on at its end ends (UK time), if there is one.</summary>
    public DateTime? InsuranceEndDate { get; init; }
    /// <summary>The insurance renewal date: the last day the latest policy covers.</summary>
    public DateOnly? InsuranceDueDate => InsuranceEndDate is { } end ? InsurancePolicyView.LastDay(end) : null;
    public DateOnly? ServiceDueDate { get; init; }
    public int? ServiceDueMileage { get; init; }
    public int? ServiceIntervalMonths { get; init; }
    public int? ServiceIntervalMiles { get; init; }
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;

    public string DisplayName => $"{Make} {Model}".Trim();

    /// <summary>Whether it's gone by <paramref name="today"/>: from its disposal date it has no due dates or reminders.</summary>
    public bool IsDisposed(DateOnly today) => DisposedDate <= today;

    /// <summary>e.g. "Sold 12/03/2026", or null if it hasn't been disposed of.</summary>
    public string? DisposalText => DisposalMethod is { } method && DisposedDate is { } date ? $"{DisposalNames(method)} {date:dd/MM/yyyy}" : null;

    /// <summary>What it cost to own, from purchase to disposal, when both prices are known.</summary>
    public decimal? Depreciation => PurchasePrice is { } bought && DisposalPrice is { } got ? bought - got : null;

    public static string DisposalNames(DisposalMethod method) => method switch
    {
        Models.DisposalMethod.PartExchanged => "Part exchanged",
        Models.DisposalMethod.WrittenOff => "Written off",
        _ => method.ToString()
    };

    /// <summary>What the disposal price is called for each way of disposing of a vehicle.</summary>
    public static string DisposalPriceNames(DisposalMethod method) => method switch
    {
        Models.DisposalMethod.PartExchanged => "Part-exchange value",
        Models.DisposalMethod.WrittenOff => "Insurance payout",
        Models.DisposalMethod.Scrapped => "Scrap value",
        _ => "Sale price"
    };

    /// <summary>
    /// The due dates that are set, soonest first, as of <paramref name="today"/>. A service due by mileage needs
    /// the vehicle's <paramref name="mileage"/> trend to estimate its date.
    /// </summary>
    public List<DueDateView> DueDates(DateOnly today, MileageTrend? mileage = null) =>
        IsDisposed(today) ? [] : DueDateView.For(this, today, mileage);

    public static readonly Expression<Func<Vehicle, VehicleView>> Projection = v => new VehicleView
    {
        Id = v.Id,
        Type = v.Type,
        FuelType = v.FuelType,
        Make = v.Make,
        Model = v.Model,
        RegistrationNumber = v.RegistrationNumber,
        RegistrationDate = v.RegistrationDate,
        PurchasedDate = v.PurchasedDate,
        PurchasePrice = v.PurchasePrice,
        DisposalMethod = v.DisposalMethod,
        DisposedDate = v.DisposedDate,
        DisposalPrice = v.DisposalPrice,
        MotDueDate = v.MotDueDate,
        TaxDueDate = v.TaxDueDate,
        // A vehicle switched off a policy mid-term isn't due to renew it.
        InsuranceEndDate = v.InsurancePeriods.Where(p => p.To == p.Policy!.EndDate).Max(p => (DateTime?)p.To),
        ServiceDueDate = v.ServiceDueDate,
        ServiceDueMileage = v.ServiceDueMileage,
        ServiceIntervalMonths = v.ServiceIntervalMonths,
        ServiceIntervalMiles = v.ServiceIntervalMiles,
        OwnerId = v.OwnerId,
        OwnerName = v.Owner!.Name
    };
}

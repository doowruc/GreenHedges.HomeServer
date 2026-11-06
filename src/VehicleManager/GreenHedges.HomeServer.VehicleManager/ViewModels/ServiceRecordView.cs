using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class ServiceRecordView
{
    public int Id { get; init; }
    public int VehicleId { get; init; }
    public DateOnly Date { get; init; }
    public int Mileage { get; init; }
    public string Garage { get; init; } = string.Empty;
    public string? GarageLocation { get; init; }
    public bool UnderPreviousOwner { get; init; }
    public bool MileageEstimated { get; init; }

    /// <summary>The garage and its location, e.g. "Main Dealer, Leeds".</summary>
    public string GarageAndLocation => GarageLocation is { } location ? $"{Garage}, {location}" : Garage;

    /// <summary>
    /// When this service was due: the service interval after the one before it (set by <c>ServiceSchedule.SetDue</c>).
    /// Null for the first service, or without that interval.
    /// </summary>
    public DateOnly? DueDate { get; set; }
    public int? DueMileage { get; set; }

    /// <summary>E.g. "2 months late" or "12 days late", if it was done after <see cref="DueDate"/>.</summary>
    public string? LateText => DueDate is { } due && Date > due ? ServiceSchedule.LateBy(due, Date) : null;

    /// <summary>E.g. "1,200 miles over", if it was done after <see cref="DueMileage"/>.</summary>
    public string? MilesOverText => DueMileage is { } due && Mileage > due ? $"{Mileage - due:N0} miles over" : null;

    public static readonly Expression<Func<ServiceRecord, ServiceRecordView>> Projection = s => new ServiceRecordView
    {
        Id = s.Id,
        VehicleId = s.VehicleId,
        Date = s.Date,
        Mileage = s.Mileage,
        Garage = s.Garage,
        GarageLocation = s.GarageLocation,
        UnderPreviousOwner = s.UnderPreviousOwner,
        MileageEstimated = s.MileageEstimated
    };
}

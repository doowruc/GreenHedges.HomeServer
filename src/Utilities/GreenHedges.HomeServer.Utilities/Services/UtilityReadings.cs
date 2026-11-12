using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>A utility's meter, for its page.</summary>
public sealed record MeterSummary(int Id, MeterUnit Unit, string? SerialNumber, DateOnly? InstalledOn, decimal? StartReading, int Readings)
{
    public MeterInfo Info => new(Id, InstalledOn, StartReading);
}

/// <summary>A utility's meters (oldest first) and its readings worked out (oldest first).</summary>
public sealed record UtilityHistory(UtilityType Utility, List<MeterSummary> Meters, List<ReadingRow> Rows)
{
    /// <summary>The current meter's unit; the utility's usual one before it has a meter.</summary>
    public MeterUnit Unit => Meters.Count > 0 ? Meters[^1].Unit : Utility.DefaultUnit();

    public ReadingRow? Latest => Rows.Count > 0 ? Rows[^1] : null;
}

/// <summary>Loads the signed-in user's readings for a utility and works them out (<see cref="ReadingCalculations"/>).</summary>
public class UtilityReadings(AppDbContext db)
{
    public async Task<UtilityHistory> LoadAsync(UtilityType utility) => (await LoadAsync([utility]))[0];

    /// <summary>Every utility, in the enum's order.</summary>
    public Task<List<UtilityHistory>> LoadAllAsync() => LoadAsync(Enum.GetValues<UtilityType>());

    private async Task<List<UtilityHistory>> LoadAsync(UtilityType[] utilities)
    {
        var meters = await db.Meters.Where(m => utilities.Contains(m.Utility))
            .Select(m => new { m.Utility, Summary = new MeterSummary(m.Id, m.Unit, m.SerialNumber, m.InstalledOn, m.StartReading, m.Readings.Count) })
            .ToListAsync();
        var readings = await db.Readings.Where(r => utilities.Contains(r.Meter!.Utility))
            .Select(r => new { r.Meter!.Utility, Info = new ReadingInfo(r.Id, r.MeterId, r.Date, r.Value) })
            .ToListAsync();

        return utilities.Select(utility =>
        {
            var own = meters.Where(m => m.Utility == utility).Select(m => m.Summary)
                .OrderBy(m => m.InstalledOn ?? DateOnly.MinValue).ToList();
            var rows = ReadingCalculations.Calculate(own.Select(m => m.Info), readings.Where(r => r.Utility == utility).Select(r => r.Info));
            return new UtilityHistory(utility, own, rows);
        }).ToList();
    }
}

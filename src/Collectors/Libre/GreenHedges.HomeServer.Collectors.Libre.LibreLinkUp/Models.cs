namespace GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;

/// <summary>A single glucose measurement as returned by LibreLinkUp.</summary>
/// <param name="TimestampUtc">From <c>FactoryTimestamp</c>, which is UTC.</param>
/// <param name="TimestampLocal"><c>Timestamp</c> exactly as received (local time, no zone).</param>
/// <param name="RawJson">The original measurement object, so derived data can be rebuilt.</param>
public sealed record GlucoseReading(
    DateTimeOffset TimestampUtc,
    string? TimestampLocal,
    double ValueMgPerDl,
    int? TrendArrow,
    int? MeasurementColor,
    bool IsHigh,
    bool IsLow,
    string RawJson);

/// <summary>The sensor currently attached to a connection.</summary>
/// <param name="Serial">Sensor serial number (<c>sn</c>).</param>
/// <param name="ActivatedAt">Activation time (<c>a</c>, Unix seconds).</param>
/// <param name="WarmupMinutes">Warm-up length in minutes (<c>w</c>; 60 on a Libre 2 Plus, confirmed on a real sensor change).</param>
/// <param name="ProductType">Sensor product type (<c>pt</c>).</param>
/// <param name="RawJson">The original <c>sensor</c> object.</param>
public sealed record SensorInfo(
    string Serial,
    DateTimeOffset? ActivatedAt,
    int? WarmupMinutes,
    int? ProductType,
    string RawJson);

public sealed record Connection(
    string PatientId,
    string? FirstName,
    string? LastName,
    GlucoseReading? Current,
    SensorInfo? Sensor = null);

/// <param name="Current">The connection's current measurement at the time of the call.</param>
/// <param name="History">About 12 hours of history at 15-minute resolution.</param>
public sealed record GraphData(GlucoseReading? Current, IReadOnlyList<GlucoseReading> History);

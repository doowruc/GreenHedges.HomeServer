namespace GreenHedges.HomeServer.Collectors.Weather.History;

/// <summary>
/// Weather Underground's history of the station (what the console uploads), fetched with the station owner's API
/// key: from <see cref="From"/>, every day not yet fetched in full, at most <see cref="MaxCallsPerDay"/> a day.
/// </summary>
public sealed class WundergroundOptions
{
    public const string SectionName = "Wunderground";

    /// <summary>The station's id on Weather Underground (e.g. ILONDON123). Keep it in the env file.</summary>
    public string StationId { get; set; } = "";

    /// <summary>The station owner's API key (wunderground.com, My Profile, API Keys). Env file only.</summary>
    public string ApiKey { get; set; } = "";

    public string BaseUrl { get; set; } = "https://api.weather.com";

    /// <summary>The first local day to fetch: the station's first upload.</summary>
    public DateOnly From { get; set; } = new(2021, 11, 7);

    /// <summary>The station's time zone: Weather Underground's days are local days.</summary>
    public string TimeZone { get; set; } = "Europe/London";

    /// <summary>When the nightly fetch runs (local time). It also runs a few minutes after startup.</summary>
    public TimeSpan RunAt { get; set; } = new(4, 30, 0);

    /// <summary>
    /// The most calls in any 24 hours, counting the days already fetched in that time, so restarts can't exceed it.
    /// Below Weather Underground's limit for owner keys (1,500 a day).
    /// </summary>
    public int MaxCallsPerDay { get; set; } = 1000;

    /// <summary>The pause between calls: Weather Underground allows 30 a minute.</summary>
    public TimeSpan CallInterval { get; set; } = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// A day is fetched again until it has been fetched this long after it ended, to pick up late uploads (so
    /// yesterday is fetched twice: the first night, and the next).
    /// </summary>
    public TimeSpan CompleteAfter { get; set; } = TimeSpan.FromHours(24);

    public bool Enabled => !string.IsNullOrWhiteSpace(StationId) && !string.IsNullOrWhiteSpace(ApiKey);
}

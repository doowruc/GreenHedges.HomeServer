using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.Collectors.Weather.Storage;

namespace GreenHedges.HomeServer.Collectors.Weather.History;

/// <summary>What reading one export found.</summary>
public sealed record WeathercloudParseResult(
    IReadOnlyList<WeathercloudRow> Rows,
    string TimeZone,
    int RowsRead,
    int SkippedEmpty,
    int SkippedInvalidTime,
    int SkippedUnparseable);

/// <summary>
/// Reads a Weathercloud CSV export: <c>;</c>-separated, usually UTF-16LE without a byte-order mark, one row per 10
/// minutes in local time (<c>dd/MM/yyyy HH:mm:ss</c>, the zone named in the first header, e.g.
/// <c>Date (Europe/London)</c>), numbers with a thousands comma (<c>1,007.9</c>), rain as the local day's running total.
/// Headers come short (<c>temp (°C)</c>) or long (<c>Temperature (°C)</c>); columns are found by name, and values are
/// converted to °C, m/s, hPa and mm from the units in the headers. In the autumn the repeated hour is in the file twice,
/// in order, so each time is the first possible instant after the row before (rows are in time order).
/// </summary>
public static partial class WeathercloudParser
{
    private const string LocalTimeFormat = "dd/MM/yyyy HH:mm:ss";

    // Short (2021 onwards, and the yearly zips) and long (single-month downloads, 2026) header names, to fields.
    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tempin"] = "temp_in", ["inside temperature"] = "temp_in",
        ["temp"] = "temp", ["temperature"] = "temp",
        ["dew"] = "dew", ["dew point"] = "dew",
        ["humin"] = "hum_in", ["inside humidity"] = "hum_in",
        ["hum"] = "hum", ["humidity"] = "hum",
        ["wspdhi"] = "gust", ["wind gust"] = "gust",
        ["wspdavg"] = "wind", ["average wind speed"] = "wind",
        ["wdiravg"] = "dir", ["average wind direction"] = "dir",
        ["bar"] = "pressure", ["atmospheric pressure"] = "pressure",
        ["rain"] = "rain",
        ["rainrate"] = "rain_rate", ["rain rate"] = "rain_rate",
    };

    public static WeathercloudParseResult Parse(Stream stream)
    {
        var lines = Decode(stream).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count == 0 || HeaderZone().Match(lines[0]) is not { Success: true } zoneMatch)
        {
            throw new FormatException("Not a Weathercloud export: the first column should be \"Date (<time zone>)\".");
        }

        var zoneId = zoneMatch.Groups[1].Value;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var headers = lines[0].Split(';');
        var fields = new Dictionary<string, (int Index, string Unit)>();
        for (var i = 1; i < headers.Length; i++)
        {
            var match = HeaderName().Match(headers[i].Trim());
            if (match.Success && Columns.TryGetValue(match.Groups["name"].Value.Trim(), out var field))
            {
                fields.TryAdd(field, (i, match.Groups["unit"].Value.Trim()));
            }
        }

        if (!fields.ContainsKey("temp") || !fields.ContainsKey("rain"))
        {
            throw new FormatException("Not a Weathercloud export: no temperature or rain column.");
        }

        var rows = new List<WeathercloudRow>();
        int read = 0, empty = 0, invalidTime = 0, unparseable = 0;
        DateTimeOffset? previous = null;
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }

            read++;
            var cells = line.Split(';');
            if (fields.Values.All(f => f.Index >= cells.Length || cells[f.Index].Trim().Length == 0))
            {
                empty++;
                continue;
            }

            if (!DateTime.TryParseExact(cells[0].Trim(), LocalTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                unparseable++;
                continue;
            }

            if (ToUtc(local, zone, previous) is not { } utc)
            {
                invalidTime++;
                continue;
            }

            try
            {
                rows.Add(new WeathercloudRow(
                    utc, cells[0].Trim(), line,
                    Temperature(cells, fields, "temp_in"), Temperature(cells, fields, "temp"), Temperature(cells, fields, "dew"),
                    Value(cells, fields, "hum_in"), Value(cells, fields, "hum"),
                    Speed(cells, fields, "gust"), Speed(cells, fields, "wind"), Value(cells, fields, "dir"),
                    Pressure(cells, fields), Length(cells, fields, "rain"), Length(cells, fields, "rain_rate")));
                previous = utc;
            }
            catch (FormatException)
            {
                unparseable++;
            }
        }

        return new WeathercloudParseResult(rows, zoneId, read, empty, invalidTime, unparseable);
    }

    /// <summary>
    /// A local time to UTC: the first possible instant after <paramref name="previous"/> (for the repeated autumn hour,
    /// summer time then winter time); null for a time that doesn't exist (the skipped spring hour).
    /// </summary>
    internal static DateTimeOffset? ToUtc(DateTime local, TimeZoneInfo zone, DateTimeOffset? previous)
    {
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        var offsets = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).OrderByDescending(o => o).ToArray() // the larger offset is the earlier instant
            : [zone.GetUtcOffset(local)];
        var candidates = offsets.Select(o => new DateTimeOffset(local, o).ToUniversalTime()).ToArray();
        return candidates.FirstOrDefault(c => previous is null || c > previous, candidates[0]);
    }

    private static string Decode(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var encoding = bytes switch
        {
            [0xFF, 0xFE, ..] => Encoding.Unicode,
            [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
            [0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8,
            [_, 0x00, ..] => Encoding.Unicode,          // UTF-16LE without a byte-order mark, as Weathercloud writes it
            _ => Encoding.UTF8,
        };
        return encoding.GetString(bytes).TrimStart('﻿');
    }

    private static double? Value(string[] cells, Dictionary<string, (int Index, string Unit)> fields, string field)
    {
        if (!fields.TryGetValue(field, out var f) || f.Index >= cells.Length)
        {
            return null;
        }

        var text = cells[f.Index].Trim().Replace(",", "", StringComparison.Ordinal);
        return text.Length == 0
            ? null
            : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double? Temperature(string[] cells, Dictionary<string, (int Index, string Unit)> fields, string field) =>
        Value(cells, fields, field) is { } v && fields[field].Unit.Contains('F') ? Math.Round((v - 32) * 5 / 9, 2) : Value(cells, fields, field);

    private static double? Speed(string[] cells, Dictionary<string, (int Index, string Unit)> fields, string field)
    {
        if (Value(cells, fields, field) is not { } v)
        {
            return null;
        }

        var ms = fields[field].Unit.ToLowerInvariant() switch
        {
            "mph" => v * 0.44704,
            "km/h" or "kmh" => v / 3.6,
            "kn" or "knots" or "kt" => v * 0.514444,
            "m/s" => v,
            var unit => throw new FormatException($"Unknown wind speed unit \"{unit}\"."),
        };
        return Math.Round(ms, 3);
    }

    private static double? Pressure(string[] cells, Dictionary<string, (int Index, string Unit)> fields) =>
        Value(cells, fields, "pressure") is { } v && fields["pressure"].Unit.Equals("inHg", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(v * 33.8639, 1)
            : Value(cells, fields, "pressure");

    private static double? Length(string[] cells, Dictionary<string, (int Index, string Unit)> fields, string field) =>
        Value(cells, fields, field) is { } v && fields[field].Unit.StartsWith("in", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(v * 25.4, 2)
            : Value(cells, fields, field);

    [GeneratedRegex(@"^\s*Date \(([^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderZone();

    [GeneratedRegex(@"^(?<name>[^(]+?)\s*(\((?<unit>[^)]*)\))?$")]
    private static partial Regex HeaderName();
}

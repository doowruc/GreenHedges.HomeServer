using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// A UK wall-clock time to the minute (insurance policies' start and end), written as "yyyy-MM-ddTHH:mm" with no offset,
/// unlike the UTC audit times. Reads any ISO 8601 time, keeping its wall-clock time as it is.
/// </summary>
public sealed class WallClockJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        if (reader.TryGetDateTimeOffset(out var withOffset))
            return DateTime.SpecifyKind(withOffset.DateTime, DateTimeKind.Unspecified);
        throw new JsonException($"\"{text}\" isn't a date and time.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

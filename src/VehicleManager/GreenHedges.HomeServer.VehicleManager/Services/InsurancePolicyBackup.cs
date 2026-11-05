using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An insurance policy (see <see cref="InsurancePolicy"/>).</summary>
public sealed record InsurancePolicyBackup : BackupRow
{
    public required int Id { get; init; }
    public required int VehicleId { get; init; }

    /// <summary>UK wall-clock times, written without an offset (unlike the UTC audit times).</summary>
    [JsonConverter(typeof(WallClockConverter))]
    public required DateTime StartDate { get; init; }

    [JsonConverter(typeof(WallClockConverter))]
    public required DateTime EndDate { get; init; }

    public required string Provider { get; set; }
    public required string PolicyNumber { get; set; }
    public InsuranceCover Cover { get; init; }
    public required decimal Premium { get; init; }
    public decimal? Excess { get; init; }
    public string? NamedDrivers { get; set; }
    public string? Notes { get; init; }

    /// <summary>"yyyy-MM-ddTHH:mm" with no offset; reads any ISO 8601 time, keeping its wall-clock time as it is.</summary>
    private sealed class WallClockConverter : JsonConverter<DateTime>
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
}

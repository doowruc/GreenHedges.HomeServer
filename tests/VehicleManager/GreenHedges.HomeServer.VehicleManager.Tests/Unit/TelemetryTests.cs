using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class TelemetryTests
{
    [Theory]
    [InlineData("https://history.mot.api.gov.uk/v1/trade/vehicles/registration/AB12CDE",
                "https://history.mot.api.gov.uk/v1/trade/vehicles/registration/{registration}")]
    [InlineData("https://history.mot.api.gov.uk/v1/trade/vehicles/registration/AB12%20CDE?x=1",
                "https://history.mot.api.gov.uk/v1/trade/vehicles/registration/{registration}?x=1")]
    [InlineData("https://driver-vehicle-licensing.api.gov.uk/vehicle-enquiry/v1/vehicles",
                "https://driver-vehicle-licensing.api.gov.uk/vehicle-enquiry/v1/vehicles")]   // the DVLA's is in the body, not the URL
    public void Registrations_are_taken_out_of_traced_URLs(string url, string traced) =>
        Assert.Equal(traced, Telemetry.RedactUrl(new Uri(url)));

}

using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Pages;

public class ImportPagesTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseFact]
    public async Task The_upload_page_has_no_error_box_until_there_is_an_error() =>
        Assert.DoesNotContain("alert-danger", (await app.ClientFor(UserId).GetAsync("/Import")).Html);
}

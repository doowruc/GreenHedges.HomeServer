using System.Net;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Pages;

public class SecurityTests(AppFactory app) : DatabaseTest, IClassFixture<AppFactory>
{
    [DatabaseTheory]
    [InlineData("/")]
    [InlineData("/People")]
    [InlineData("/People/Create")]
    [InlineData("/People/1/Employers")]
    [InlineData("/People/1/PayElements")]
    public async Task Pages_need_a_signed_in_user(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientFor(null).GetAsync(url)).Status);

    [DatabaseTheory]
    [InlineData("/Account/SignedOut")]
    [InlineData("/health")]
    public async Task Some_pages_are_open_to_anyone(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await app.ClientFor(null).GetAsync(url)).Status);

    [DatabaseFact]
    public async Task Another_users_person_and_what_they_have_are_not_found()
    {
        int personId, employerId, elementId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            (personId, employerId, elementId) = (person.Id, person.Employers[0].Id, person.PayElements[0].Id);
        }

        var stranger = app.ClientFor(NewUser());
        foreach (var url in new[]
                 {
                     $"/People/Edit/{personId}", $"/People/Delete/{personId}",
                     $"/People/{personId}/Employers", $"/People/{personId}/Employers/Create", $"/People/{personId}/Employers/Edit/{employerId}",
                     $"/People/{personId}/PayElements", $"/People/{personId}/PayElements/Create", $"/People/{personId}/PayElements/Edit/{elementId}"
                 })
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(url)).Status);
    }

    [DatabaseFact]
    public async Task An_employer_is_only_found_under_its_own_person()
    {
        int otherPersonId, employerId;
        await using (var db = Db())
        {
            employerId = (await TestData.AddPersonAsync(db, "Alex")).Employers[0].Id;
            otherPersonId = (await TestData.AddPersonAsync(db, "Sam")).Id;
        }

        var client = app.ClientFor(UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/People/{otherPersonId}/Employers/Edit/{employerId}")).Status);
    }
}

using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Data;

public class PerUserDataTests : DatabaseTest
{
    [DatabaseFact]
    public async Task New_people_belong_to_the_signed_in_user()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        Assert.Equal(UserId, person.UserId);
        Assert.Equal(UserId, person.Employers[0].AddedBy);
    }

    [DatabaseFact]
    public async Task Users_only_see_their_own_data()
    {
        int personId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            personId = person.Id;
            await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 3000m), ("Tax", 400m));
        }

        await using var other = TestDatabase.As(NewUser());
        Assert.False(await other.People.AnyAsync());
        Assert.False(await other.Employers.AnyAsync());
        Assert.False(await other.PayElements.AnyAsync());
        Assert.False(await other.Payslips.AnyAsync());
        Assert.False(await other.PayslipLines.AnyAsync());
        Assert.Equal(0, await other.People.Where(p => p.Id == personId).ExecuteDeleteAsync());
        Assert.Equal(0, await other.PayslipLines.ExecuteUpdateAsync(s => s.SetProperty(l => l.Amount, 0m)));
    }

    [DatabaseFact]
    public async Task Different_users_can_use_the_same_names()
    {
        await using (var db = Db()) await TestData.AddPersonAsync(db, "Same");
        await using var other = TestDatabase.As(NewUser());
        await TestData.AddPersonAsync(other, "Same");   // same person, employer and element names: no clash
        Assert.Equal(1, await other.People.CountAsync());
    }

    [DatabaseFact]
    public async Task Nothing_is_visible_or_saved_without_a_signed_in_user()
    {
        await using (var db = Db()) await TestData.AddPersonAsync(db);
        await using var none = TestDatabase.As(null);
        Assert.False(await none.People.AnyAsync());
        none.People.Add(new Person { Name = "Nobody" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.SaveChangesAsync());
    }
}

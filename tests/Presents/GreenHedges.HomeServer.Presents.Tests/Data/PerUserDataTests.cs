using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Data;

public class PerUserDataTests : DatabaseTest
{
    [DatabaseFact]
    public async Task New_children_and_Christmases_belong_to_the_signed_in_user()
    {
        await using var db = Db();
        var child = await TestData.AddChildAsync(db);
        var christmas = await TestData.AddChristmasAsync(db);
        var purchase = await TestData.AddPurchaseAsync(db, christmas, child);

        Assert.Equal(UserId, child.UserId);
        Assert.Equal(UserId, christmas.UserId);
        Assert.Equal(UserId, purchase.AddedBy);
    }

    [DatabaseFact]
    public async Task Users_only_see_their_own_data()
    {
        await using (var db = Db())
            await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db), await TestData.AddChildAsync(db));

        await using var other = TestDatabase.As(NewUser());
        Assert.False(await other.Children.AnyAsync());
        Assert.False(await other.Christmases.AnyAsync());
        Assert.False(await other.Purchases.AnyAsync());
        Assert.Equal(0, await other.Purchases.ExecuteDeleteAsync());
        Assert.Equal(0, await other.Children.ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "Changed")));
    }

    [DatabaseFact]
    public async Task Nothing_is_visible_or_saved_without_a_signed_in_user()
    {
        await using (var db = Db()) await TestData.AddChildAsync(db);
        await using var none = TestDatabase.As(null);
        Assert.False(await none.Children.AnyAsync());
        none.Children.Add(new Child { Name = "Bea" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.SaveChangesAsync());
    }
}

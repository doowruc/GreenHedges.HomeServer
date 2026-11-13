using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Presents.Tests.Data;

/// <summary>What the database itself enforces, whatever the pages do.</summary>
public class SchemaTests : DatabaseTest
{
    [DatabaseFact]
    public async Task Prices_and_budgets_keep_pence()
    {
        await using (var db = Db())
        {
            var christmas = await TestData.AddChristmasAsync(db, budget: 123.45m);
            await TestData.AddPurchaseAsync(db, christmas, await TestData.AddChildAsync(db), price: 19.99m);
        }

        await using var check = Db();
        Assert.Equal(123.45m, (await check.Christmases.SingleAsync()).Budget);
        Assert.Equal(19.99m, (await check.Purchases.SingleAsync()).Price);
    }

    [DatabaseFact]
    public async Task A_user_has_one_Christmas_a_year()
    {
        await using var db = Db();
        await TestData.AddChristmasAsync(db, 2026);
        db.ChangeTracker.Clear();
        db.Christmases.Add(new Christmas { Year = 2026 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task Two_users_can_each_have_the_same_year_and_child_name()
    {
        await using (var db = Db()) { await TestData.AddChristmasAsync(db, 2026); await TestData.AddChildAsync(db, "Ash"); }
        await using var other = TestDatabase.As(NewUser());
        await TestData.AddChristmasAsync(other, 2026);
        await TestData.AddChildAsync(other, "Ash");

        Assert.Equal(1, await other.Children.CountAsync());
    }

    [DatabaseFact]
    public async Task Child_names_are_unique_per_user()
    {
        await using var db = Db();
        await TestData.AddChildAsync(db, "Ash");
        db.ChangeTracker.Clear();
        db.Children.Add(new Child { Name = "Ash" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task A_child_with_purchases_cant_be_deleted()
    {
        int childId;
        await using (var db = Db())
        {
            var child = await TestData.AddChildAsync(db);
            childId = child.Id;
            await TestData.AddPurchaseAsync(db, await TestData.AddChristmasAsync(db), child);
        }

        await using var check = Db();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => check.Children.Where(c => c.Id == childId).ExecuteDeleteAsync());
        Assert.True(await check.Purchases.AnyAsync(p => p.ChildId == childId));
    }

    [DatabaseFact]
    public async Task Deleting_a_Christmas_deletes_its_purchases()
    {
        int christmasId;
        await using (var db = Db())
        {
            var christmas = await TestData.AddChristmasAsync(db);
            christmasId = christmas.Id;
            await TestData.AddPurchaseAsync(db, christmas, await TestData.AddChildAsync(db));
        }

        await using var check = Db();
        await check.Christmases.Where(c => c.Id == christmasId).ExecuteDeleteAsync();
        Assert.False(await check.Purchases.IgnoreQueryFilters().AnyAsync(p => p.ChristmasId == christmasId));
    }
}

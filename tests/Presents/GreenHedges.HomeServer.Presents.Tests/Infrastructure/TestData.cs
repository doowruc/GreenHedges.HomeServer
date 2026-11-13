using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;

namespace GreenHedges.HomeServer.Presents.Tests.Infrastructure;

/// <summary>Builds data for the context's user. Made-up children, presents and amounts only.</summary>
public static class TestData
{
    /// <summary>A child for the context's user.</summary>
    public static async Task<Child> AddChildAsync(AppDbContext db, string name = "Ash", int displayOrder = 1)
    {
        var child = new Child { Name = name, DisplayOrder = displayOrder };
        db.Children.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    /// <summary>A Christmas for the context's user.</summary>
    public static async Task<Christmas> AddChristmasAsync(AppDbContext db, int year = 2026, decimal? budget = null)
    {
        var christmas = new Christmas { Year = year, Budget = budget };
        db.Christmases.Add(christmas);
        await db.SaveChangesAsync();
        return christmas;
    }

    /// <summary>A present for a child one Christmas.</summary>
    public static async Task<Purchase> AddPurchaseAsync(AppDbContext db, Christmas christmas, Child child, string item = "Jigsaw",
        decimal price = 10m, int quantity = 1, bool splitWithMum = false, bool splitWithBirthday = false)
    {
        var purchase = new Purchase
        {
            ChristmasId = christmas.Id, ChildId = child.Id, Item = item, Price = price, Quantity = quantity,
            SplitWithMum = splitWithMum, SplitWithBirthday = splitWithBirthday
        };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();
        return purchase;
    }
}

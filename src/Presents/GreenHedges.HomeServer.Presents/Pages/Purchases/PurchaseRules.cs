using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.Models;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Purchases;

internal static class PurchaseRules
{
    /// <summary>
    /// Tidies the text (spaces trimmed and runs made one; an empty shop is none) and checks what the attributes can't:
    /// the child is one of the user's, the price has whole pence, and the date isn't in the future.
    /// </summary>
    public static async Task CheckAsync(AppDbContext db, PurchaseInput purchase, DateOnly today, ModelStateDictionary state)
    {
        purchase.Item = Tidy(purchase.Item) ?? string.Empty;
        purchase.Shop = Tidy(purchase.Shop);

        if (purchase.ChildId is { } childId && !await db.Children.AnyAsync(c => c.Id == childId))
            state.AddModelError("Purchase.ChildId", "Choose who it's for.");
        if (purchase.Price is { } price && price != Math.Round(price, 2))
            state.AddModelError("Purchase.Price", "The price is in pounds and pence: at most two decimal places.");
        if (purchase.Date > today)
            state.AddModelError("Purchase.Date", "The date can't be in the future.");
    }

    /// <summary>The Christmas for <paramref name="year"/>, added (unsaved, with no budget) if there isn't one yet.</summary>
    public static async Task<Christmas> ChristmasForAsync(AppDbContext db, int year)
    {
        var christmas = await db.Christmases.FirstOrDefaultAsync(c => c.Year == year);
        if (christmas is not null) return christmas;
        christmas = new Christmas { Year = year };
        db.Christmases.Add(christmas);
        return christmas;
    }

    private static string? Tidy(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } tidy ? tidy : null;
}

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Presents.Data;
using GreenHedges.HomeServer.Presents.ViewModels;

namespace GreenHedges.HomeServer.Presents.Pages.Children;

internal static class ChildRules
{
    /// <summary>
    /// Tidies the name (spaces trimmed, runs of spaces made one) and checks it's unique, ignoring case
    /// (<paramref name="id"/> is null for a new child).
    /// </summary>
    public static async Task ValidateAsync(AppDbContext db, int? id, ChildInput child, ModelStateDictionary state)
    {
        child.Name = string.Join(' ', (child.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var name = child.Name.ToLower();
        if (await db.Children.AnyAsync(c => c.Id != id && c.Name.ToLower() == name))
            state.AddModelError("Child.Name", "A child with this name already exists.");
    }
}

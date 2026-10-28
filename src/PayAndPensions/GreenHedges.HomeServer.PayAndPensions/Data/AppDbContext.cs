using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's people and
/// (through their person) employers, pay elements, payslips and payslip lines.
/// Saving stamps new people with the current user's id and fills in every table's audit columns
/// (<see cref="AuditedDbContext"/>). With no signed-in user (e.g. at startup) queries return nothing and saving throws. Use
/// <c>IgnoreQueryFilters()</c> only for deliberate cross-user work.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? currentUser = null)
    : AuditedDbContext(options, currentUser)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Employer> Employers => Set<Employer>();
    public DbSet<PayElement> PayElements => Set<PayElement>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayslipLine> PayslipLines => Set<PayslipLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Deleting a person takes their employers and pay elements with them; payslips must go first (Restrict), so
        // nobody's pay history goes by accident. SQL Server allows only one cascade path to a table, which this keeps.
        modelBuilder.Entity<Person>(e =>
        {
            e.HasQueryFilter(p => p.UserId == CurrentUserId);
            e.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
            e.HasMany(p => p.Employers)
             .WithOne(m => m.Person)
             .HasForeignKey(m => m.PersonId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.PayElements)
             .WithOne(el => el.Person)
             .HasForeignKey(el => el.PersonId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Payslips)
             .WithOne(s => s.Person)
             .HasForeignKey(s => s.PersonId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Employer>(e =>
        {
            e.HasQueryFilter(m => m.Person!.UserId == CurrentUserId);
            e.HasIndex(m => new { m.PersonId, m.Name }).IsUnique();
            e.HasMany(m => m.Payslips)
             .WithOne(s => s.Employer)
             .HasForeignKey(s => s.EmployerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayElement>(e =>
        {
            e.HasQueryFilter(el => el.Person!.UserId == CurrentUserId);
            e.Property(el => el.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(el => el.Category).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(el => new { el.PersonId, el.Name }).IsUnique();
            // Elements in use can't be deleted; hide them instead.
            e.HasMany(el => el.Lines)
             .WithOne(l => l.PayElement)
             .HasForeignKey(l => l.PayElementId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payslip>(e =>
        {
            e.HasQueryFilter(s => s.Person!.UserId == CurrentUserId);
            e.HasIndex(s => new { s.PersonId, s.TaxYear, s.TaxMonth }).IsUnique();
            e.HasIndex(s => s.EmployerId);
            e.ToTable(t => t.HasCheckConstraint("CK_Payslips_TaxMonth", "[TaxMonth] BETWEEN 1 AND 12"));
            e.HasMany(s => s.Lines)
             .WithOne(l => l.Payslip)
             .HasForeignKey(l => l.PayslipId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PayslipLine>(e =>
        {
            e.HasQueryFilter(l => l.Payslip!.Person!.UserId == CurrentUserId);
            e.Property(l => l.Amount).HasPrecision(19, 2);
            e.HasIndex(l => new { l.PayslipId, l.PayElementId }).IsUnique();
        });
    }
}

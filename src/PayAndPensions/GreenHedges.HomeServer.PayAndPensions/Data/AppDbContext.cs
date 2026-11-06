using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Data;

/// <remarks>
/// Each signed-in user has their own data. Query filters limit every query to the current user's people and pension
/// providers, and (through their person) employers, pay elements, payslips, payslip lines, pensions, statement types
/// and statements.
/// Saving stamps new people and pension providers with the current user's id and fills in every table's audit columns
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
    public DbSet<PensionProvider> PensionProviders => Set<PensionProvider>();
    public DbSet<Pension> Pensions => Set<Pension>();
    public DbSet<StatementType> StatementTypes => Set<StatementType>();
    public DbSet<PensionStatement> PensionStatements => Set<PensionStatement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Deleting a person takes their employers, pay elements and pensions with them; payslips must go first (Restrict), so
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
            e.HasMany(p => p.Pensions)
             .WithOne(pn => pn.Person)
             .HasForeignKey(pn => pn.PersonId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // A provider is shared by the user's people, so it can't be deleted while any of their pensions are with it.
        modelBuilder.Entity<PensionProvider>(e =>
        {
            e.HasQueryFilter(pp => pp.UserId == CurrentUserId);
            e.HasIndex(pp => new { pp.UserId, pp.Name }).IsUnique();
            e.HasMany(pp => pp.Pensions)
             .WithOne(pn => pn.PensionProvider)
             .HasForeignKey(pn => pn.PensionProviderId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Pension>(e =>
        {
            e.HasQueryFilter(pn => pn.Person!.UserId == CurrentUserId);
            e.Property(pn => pn.Type).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(pn => new { pn.PersonId, pn.Name }).IsUnique();
            e.HasIndex(pn => pn.PensionProviderId);
            // Not while payslips go to it: pay history is never changed by deleting something else.
            e.HasMany(pn => pn.Payslips)
             .WithOne(s => s.Pension)
             .HasForeignKey(s => s.PensionId)
             .OnDelete(DeleteBehavior.Restrict);
            // Its statement types go with it, but not while they have statements (Restrict below): a pension's history
            // is never deleted along with something else either.
            e.HasMany(pn => pn.StatementTypes)
             .WithOne(t => t.Pension)
             .HasForeignKey(t => t.PensionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatementType>(e =>
        {
            e.HasQueryFilter(t => t.Pension!.Person!.UserId == CurrentUserId);
            e.HasIndex(t => new { t.PensionId, t.Name }).IsUnique();
            e.HasMany(t => t.Statements)
             .WithOne(st => st.StatementType)
             .HasForeignKey(st => st.StatementTypeId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PensionStatement>(e =>
        {
            e.HasQueryFilter(st => st.StatementType!.Pension!.Person!.UserId == CurrentUserId);
            e.Property(st => st.Value).HasPrecision(19, 2);
            e.Property(st => st.ProjectedFund).HasPrecision(19, 2);
            e.Property(st => st.ProjectedAnnualIncome).HasPrecision(19, 2);
            e.HasIndex(st => new { st.StatementTypeId, st.Date }).IsUnique();
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

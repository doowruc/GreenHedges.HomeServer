using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Data;

/// <summary>What the database itself enforces, whatever the pages do.</summary>
public class SchemaTests : DatabaseTest
{
    [DatabaseFact]
    public async Task Amounts_keep_their_pence_and_can_be_negative()
    {
        int payslipId;
        await using (var db = Db())
        {
            var person = await TestData.AddPersonAsync(db);
            payslipId = (await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 2345.67m), ("Pension", -12.34m))).Id;
        }

        await using var check = Db();
        var amounts = await check.PayslipLines.Where(l => l.PayslipId == payslipId).OrderBy(l => l.Amount).Select(l => l.Amount).ToListAsync();
        Assert.Equal([-12.34m, 2345.67m], amounts);
    }

    [DatabaseFact]
    public async Task A_person_has_one_payslip_per_tax_month()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 2m)));
    }

    [DatabaseTheory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task A_tax_month_is_1_to_12(int taxMonth)
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        db.Payslips.Add(new Payslip { PersonId = person.Id, EmployerId = person.Employers[0].Id, TaxYear = 2026, TaxMonth = taxMonth, PayDate = new DateOnly(2026, 4, 25) });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task Deleting_a_person_takes_their_employers_and_pay_elements()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);

        await db.People.Where(p => p.Id == person.Id).ExecuteDeleteAsync();

        Assert.False(await db.Employers.IgnoreQueryFilters().AnyAsync(m => m.PersonId == person.Id));
        Assert.False(await db.PayElements.IgnoreQueryFilters().AnyAsync(el => el.PersonId == person.Id));
    }

    [DatabaseFact]
    public async Task Pay_history_is_never_deleted_along_with_something_else()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m));

        // A person with payslips, an employer with payslips, and an element on a payslip all stay.
        await Assert.ThrowsAnyAsync<Exception>(() => db.People.Where(p => p.Id == person.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => db.Employers.Where(m => m.PersonId == person.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => db.PayElements.Where(el => el.Name == "Salary" && el.PersonId == person.Id).ExecuteDeleteAsync());
        Assert.Equal(1, await db.PayslipLines.CountAsync());
    }

    [DatabaseFact]
    public async Task Deleting_a_payslip_takes_its_lines()
    {
        await using var db = Db();
        var person = await TestData.AddPersonAsync(db);
        var payslip = await TestData.AddPayslipAsync(db, person, 2026, 1, ("Salary", 1m), ("Tax", 1m));

        await db.Payslips.Where(s => s.Id == payslip.Id).ExecuteDeleteAsync();

        Assert.False(await db.PayslipLines.AnyAsync());
    }
}

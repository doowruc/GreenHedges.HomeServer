using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>Builds data for the context's user. Made-up names and figures only.</summary>
public static class TestData
{
    /// <summary>A person with one employer and the usual pay elements: Salary, Tax, NI, Pension and Employer pension.</summary>
    public static async Task<Person> AddPersonAsync(AppDbContext db, string name = "Alex")
    {
        var person = new Person
        {
            Name = name,
            Employers = [new Employer { Name = "Example Ltd" }],
            PayElements =
            [
                new PayElement { Name = "Salary", Kind = PayElementKind.Earning, Category = PayCategory.Salary, SortOrder = 10 },
                new PayElement { Name = "Tax", Kind = PayElementKind.Deduction, Category = PayCategory.Tax, SortOrder = 20 },
                new PayElement { Name = "NI", Kind = PayElementKind.Deduction, Category = PayCategory.NationalInsurance, SortOrder = 30 },
                new PayElement { Name = "Pension", Kind = PayElementKind.Deduction, Category = PayCategory.Pension, SortOrder = 40 },
                new PayElement { Name = "Employer pension", Kind = PayElementKind.EmployerContribution, Category = PayCategory.Pension, SortOrder = 50 },
            ]
        };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    /// <summary>A payslip for <paramref name="person"/> from their first employer, with an amount for each named element.</summary>
    public static async Task<Payslip> AddPayslipAsync(AppDbContext db, Person person, int taxYear, int taxMonth,
        params (string Element, decimal Amount)[] lines)
    {
        var payslip = new Payslip
        {
            PersonId = person.Id,
            EmployerId = person.Employers[0].Id,
            TaxYear = taxYear,
            TaxMonth = taxMonth,
            PayDate = new DateOnly(taxMonth <= 9 ? taxYear : taxYear + 1, (taxMonth + 2) % 12 + 1, 25),
            Lines = lines.Select(l => new PayslipLine
            {
                PayElementId = person.PayElements.Single(el => el.Name == l.Element).Id,
                Amount = l.Amount
            }).ToList()
        };
        db.Payslips.Add(payslip);
        await db.SaveChangesAsync();
        return payslip;
    }

    /// <summary>A pension provider for the context's user.</summary>
    public static async Task<PensionProvider> AddProviderAsync(AppDbContext db, string name = "Example Pensions", string? website = null)
    {
        var provider = new PensionProvider { Name = name, Website = website };
        db.PensionProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    /// <summary>A pension for <paramref name="person"/> with <paramref name="provider"/>.</summary>
    public static async Task<Pension> AddPensionAsync(AppDbContext db, Person person, PensionProvider provider,
        string name = "Workplace pension", PensionType type = PensionType.Workplace)
    {
        var pension = new Pension { PersonId = person.Id, PensionProviderId = provider.Id, Name = name, Type = type };
        db.Pensions.Add(pension);
        await db.SaveChangesAsync();
        return pension;
    }

    /// <summary>A statement type for <paramref name="pension"/>.</summary>
    public static async Task<StatementType> AddStatementTypeAsync(AppDbContext db, Pension pension, string name = "Investment report",
        bool hasProjections = false)
    {
        var type = new StatementType { PensionId = pension.Id, Name = name, HasProjections = hasProjections };
        db.StatementTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    /// <summary>A statement of <paramref name="type"/>.</summary>
    public static async Task<PensionStatement> AddStatementAsync(AppDbContext db, StatementType type, DateOnly date, decimal value,
        string? label = null, decimal? projectedFund = null, decimal? projectedAnnualIncome = null)
    {
        var statement = new PensionStatement
        {
            StatementTypeId = type.Id, Date = date, Value = value, Label = label,
            ProjectedFund = projectedFund, ProjectedAnnualIncome = projectedAnnualIncome
        };
        db.PensionStatements.Add(statement);
        await db.SaveChangesAsync();
        return statement;
    }
}

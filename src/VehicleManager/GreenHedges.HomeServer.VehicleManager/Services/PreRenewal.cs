using GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// What an insurance renewal quote asks for (<c>Pages/Insurance/PreRenewal</c>): the renewal date, the claims to declare
/// for each cut-off insurers use, and how long the licence has been held then.
/// </summary>
public static class PreRenewal
{
    /// <summary>The cut-offs insurers ask about claims within: "in the last 3 (4, 5) years".</summary>
    public static readonly int[] CutOffYears = [3, 4, 5];

    /// <summary>
    /// The day the next policy would start: when the current one (in force at <paramref name="now"/>; the latest-ending,
    /// if there are several) finishes, the next day for one ending at 23:59. Null with no current policy.
    /// </summary>
    public static DateOnly? RenewalDate(IEnumerable<(DateTime Start, DateTime End)> policies, DateTime now) =>
        policies.Where(p => p.Start <= now && now < p.End).OrderByDescending(p => p.End).Select(p => (DateTime?)p.End).FirstOrDefault() is { } end
            ? DateOnly.FromDateTime(CreateModel.RenewalStart(end))
            : null;

    /// <summary>Whether a claim on <paramref name="claimDate"/> is within the <paramref name="years"/> before <paramref name="renewal"/>, so must be declared.</summary>
    public static bool MustDeclare(DateOnly claimDate, DateOnly renewal, int years) =>
        claimDate > renewal.AddYears(-years) && claimDate <= renewal;

    /// <summary>Whole years from <paramref name="from"/> to <paramref name="to"/> (0 before the first anniversary).</summary>
    public static int FullYears(DateOnly from, DateOnly to) =>
        to.Year - from.Year - (to < from.AddYears(to.Year - from.Year) ? 1 : 0);

    /// <summary>
    /// A GB driver number's sections, as comparison sites ask for them: surname (5), date of birth (6), initials (2), a
    /// digit (1) and check letters (2), e.g. MORGA, 753116, SM, 9, IJ. Null if it isn't 16 characters.
    /// </summary>
    public static List<(string Name, string Value)>? Sections(string? driverNumber) =>
        driverNumber is { Length: 16 } n
            ? [("Surname", n[..5]), ("Date of birth", n[5..11]), ("Initials", n[11..13]), ("Digit", n[13..14]), ("Check letters", n[14..])]
            : null;

    /// <summary>A driver number in the three groups it's often written in, e.g. MORGA 753116 SM9IJ.</summary>
    public static string? Grouped(string? driverNumber) =>
        driverNumber is { Length: 16 } n ? $"{n[..5]} {n[5..11]} {n[11..]}" : null;
}

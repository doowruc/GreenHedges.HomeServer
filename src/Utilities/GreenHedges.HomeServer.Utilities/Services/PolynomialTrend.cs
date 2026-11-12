namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>
/// A polynomial trendline: the least-squares fit of a polynomial to points, as a spreadsheet chart's "polynomial"
/// trendline (the meter readings sheet used order 4). Pure.
/// </summary>
public sealed class PolynomialTrend
{
    private readonly double[] _coefficients;   // of the scaled x, lowest power first
    private readonly double _middle;
    private readonly double _halfRange;

    private PolynomialTrend(double[] coefficients, double middle, double halfRange) =>
        (_coefficients, _middle, _halfRange) = (coefficients, middle, halfRange);

    /// <summary>The polynomial's degree: the order asked for, or less with too few points for it.</summary>
    public int Order => _coefficients.Length - 1;

    /// <summary>
    /// Fits a polynomial of <paramref name="order"/> to <paramref name="points"/>; a lower order with fewer than
    /// order + 1 different x values (as many as they can pin down). Null with fewer than three, which only make a line
    /// through themselves.
    /// </summary>
    public static PolynomialTrend? Fit(IReadOnlyCollection<(double X, double Y)> points, int order = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(order, 1);
        var distinct = points.Select(p => p.X).Distinct().Count();
        if (distinct < 3) return null;
        var terms = Math.Min(order, distinct - 1) + 1;

        // x scaled to -1..1 about its middle: day numbers in the hundreds of thousands to the 8th power would swamp a double.
        var (min, max) = (points.Min(p => p.X), points.Max(p => p.X));
        var (middle, halfRange) = ((min + max) / 2, (max - min) / 2);

        // The normal equations (AᵀA)c = Aᵀy, where row i of A is 1, u, u², …
        var matrix = new double[terms, terms + 1];
        var powers = new double[2 * terms - 1];
        foreach (var (x, y) in points)
        {
            var u = (x - middle) / halfRange;
            powers[0] = 1;
            for (var k = 1; k < powers.Length; k++) powers[k] = powers[k - 1] * u;
            for (var row = 0; row < terms; row++)
            {
                for (var column = 0; column < terms; column++) matrix[row, column] += powers[row + column];
                matrix[row, terms] += powers[row] * y;
            }
        }

        return new PolynomialTrend(Solve(matrix, terms), middle, halfRange);
    }

    /// <summary>The trendline's value at <paramref name="x"/>.</summary>
    public double At(double x)
    {
        var u = (x - _middle) / _halfRange;
        var value = 0d;
        for (var k = _coefficients.Length - 1; k >= 0; k--) value = value * u + _coefficients[k];
        return value;
    }

    /// <summary>Gaussian elimination with partial pivoting on an augmented matrix.</summary>
    private static double[] Solve(double[,] m, int n)
    {
        for (var column = 0; column < n; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < n; row++)
                if (Math.Abs(m[row, column]) > Math.Abs(m[pivot, column])) pivot = row;
            if (pivot != column)
                for (var k = column; k <= n; k++) (m[column, k], m[pivot, k]) = (m[pivot, k], m[column, k]);

            for (var row = column + 1; row < n; row++)
            {
                var factor = m[row, column] / m[column, column];
                for (var k = column; k <= n; k++) m[row, k] -= factor * m[column, k];
            }
        }

        var solution = new double[n];
        for (var row = n - 1; row >= 0; row--)
        {
            var sum = m[row, n];
            for (var k = row + 1; k < n; k++) sum -= m[row, k] * solution[k];
            solution[row] = sum / m[row, row];
        }
        return solution;
    }
}

using System.Globalization;

namespace SharpCommander.Core.Utilities;

/// <summary>
/// Culture-aware, case-insensitive comparer that compares runs of ASCII digits by numeric value,
/// so "file2" sorts before "file10". Ties are broken ordinally to keep the order deterministic.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    private readonly CompareInfo _compareInfo;

    /// <summary>Gets a comparer using the current culture.</summary>
    public static NaturalStringComparer Instance { get; } = new(CultureInfo.CurrentCulture);

    /// <summary>Creates a comparer for the given culture.</summary>
    public NaturalStringComparer(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _compareInfo = culture.CompareInfo;
    }

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var ix = 0;
        var iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            var result = char.IsAsciiDigit(x[ix]) && char.IsAsciiDigit(y[iy])
                ? CompareDigitRuns(x, ref ix, y, ref iy)
                : CompareTextRuns(x, ref ix, y, ref iy);

            if (result != 0)
            {
                return result;
            }
        }

        if (ix < x.Length)
        {
            return 1;
        }

        if (iy < y.Length)
        {
            return -1;
        }

        return string.CompareOrdinal(x, y);
    }

    private static int CompareDigitRuns(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        var startY = iy;

        while (ix < x.Length && char.IsAsciiDigit(x[ix]))
        {
            ix++;
        }

        while (iy < y.Length && char.IsAsciiDigit(y[iy]))
        {
            iy++;
        }

        var digitsX = x.AsSpan(startX, ix - startX).TrimStart('0');
        var digitsY = y.AsSpan(startY, iy - startY).TrimStart('0');

        if (digitsX.Length != digitsY.Length)
        {
            return digitsX.Length < digitsY.Length ? -1 : 1;
        }

        var byValue = digitsX.SequenceCompareTo(digitsY);
        if (byValue != 0)
        {
            return byValue;
        }

        // Same value: fewer leading zeros first ("7" before "007").
        var rawLengthX = ix - startX;
        var rawLengthY = iy - startY;
        return rawLengthX.CompareTo(rawLengthY);
    }

    private int CompareTextRuns(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        var startY = iy;

        while (ix < x.Length && !char.IsAsciiDigit(x[ix]))
        {
            ix++;
        }

        while (iy < y.Length && !char.IsAsciiDigit(y[iy]))
        {
            iy++;
        }

        return _compareInfo.Compare(x, startX, ix - startX, y, startY, iy - startY, CompareOptions.IgnoreCase);
    }
}

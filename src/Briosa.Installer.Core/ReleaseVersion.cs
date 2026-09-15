namespace Briosa.Installer.Core;

/// <summary>Semantic version precedence for releases accepted by the catalog contract.</summary>
public static class ReleaseVersion
{
    public static bool IsValid(string? version) => ReleaseCatalogCodec.IsValidVersion(version);

    public static int Compare(string left, string right)
    {
        if (!IsValid(left) || !IsValid(right)) throw new ArgumentException("A valid release version is required.");
        var a = left.Split('+')[0].Split('-', 2);
        var b = right.Split('+')[0].Split('-', 2);
        var ac = a[0].Split('.');
        var bc = b[0].Split('.');
        for (var i = 0; i < 3; i++)
        {
            var result = CompareNumber(ac[i], bc[i]);
            if (result != 0) return result;
        }
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        var ap = a[1].Split('.');
        var bp = b[1].Split('.');
        for (var i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        {
            var an = ap[i].All(char.IsAsciiDigit);
            var bn = bp[i].All(char.IsAsciiDigit);
            var result = an && bn ? CompareNumber(ap[i], bp[i]) :
                an != bn ? (an ? -1 : 1) : string.CompareOrdinal(ap[i], bp[i]);
            if (result != 0) return result;
        }
        return ap.Length.CompareTo(bp.Length);
    }

    // Catalog numbers can exceed CLR integer ranges; validated numbers have no leading zeroes.
    private static int CompareNumber(string left, string right) => left.Length == right.Length
        ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length);
}

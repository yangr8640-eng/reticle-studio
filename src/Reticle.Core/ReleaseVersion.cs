using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Reticle.Core;

/// <summary>SemVer ordering, including numeric preview identifiers and ignored build metadata.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly string[] numbers;
    private readonly string[] preview;
    public string Display { get; }
    public bool IsPrerelease => preview.Length > 0;

    private ReleaseVersion(Match match)
    {
        numbers = [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
        preview = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        Display = string.Join('.', numbers) + (IsPrerelease ? "-" + string.Join('.', preview) : "");
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;
        if (value is null || value.Length > 256) return false;
        var match = Pattern.Match(value);
        if (!match.Success) return false;
        var parsed = new ReleaseVersion(match);
        if (parsed.preview.Any(x => IsNumeric(x) && x.Length > 1 && x[0] == '0')) return false;
        version = parsed;
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        for (int i = 0; i < numbers.Length; i++)
        {
            int result = CompareNumber(numbers[i], other.numbers[i]);
            if (result != 0) return result;
        }
        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (int i = 0; i < Math.Min(preview.Length, other.preview.Length); i++)
        {
            string a = preview[i], b = other.preview[i];
            bool an = IsNumeric(a), bn = IsNumeric(b);
            int result = an && bn ? CompareNumber(a, b) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
            if (result != 0) return result;
        }
        return preview.Length.CompareTo(other.preview.Length);
    }

    private static bool IsNumeric(string value) => value.All(char.IsAsciiDigit);
    private static int CompareNumber(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
}

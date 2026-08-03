namespace Stelliberty.Application.Updates;

// 支持 1.2.3 / 1.2.3-betaN：同数字版本下正式版大于预发布版
public static class AppVersionComparer
{
    public static int Compare(string? left, string? right)
    {
        var a = Parse(left);
        var b = Parse(right);
        var core = a.Core.CompareTo(b.Core);
        if (core != 0)
        {
            return core;
        }

        if (a.IsRelease && b.IsRelease)
        {
            return 0;
        }

        if (a.IsRelease)
        {
            return 1;
        }

        if (b.IsRelease)
        {
            return -1;
        }

        return ComparePreRelease(a.PreRelease, b.PreRelease);
    }

    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().TrimStart('v', 'V');

    public static bool IsValid(string? value)
    {
        var normalized = Normalize(value);
        var dash = normalized.IndexOf('-', StringComparison.Ordinal);
        var coreText = dash >= 0 ? normalized[..dash] : normalized;
        return Version.TryParse(coreText, out _) && (dash < 0 || dash < normalized.Length - 1);
    }

    public static bool IsPreRelease(string? value) => !Parse(value).IsRelease;

    private static int ComparePreRelease(string left, string right)
    {
        // beta1 < beta2 < beta10（数字后缀按数值比）
        if (TrySplitBeta(left, out var leftName, out var leftNum)
            && TrySplitBeta(right, out var rightName, out var rightNum)
            && string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase))
        {
            var byNum = leftNum.CompareTo(rightNum);
            if (byNum != 0)
            {
                return byNum;
            }
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitBeta(string value, out string name, out int number)
    {
        name = value;
        number = 0;
        var i = value.Length - 1;
        while (i >= 0 && char.IsDigit(value[i]))
        {
            i--;
        }

        if (i < 0 || i == value.Length - 1)
        {
            return false;
        }

        name = value[..(i + 1)];
        return int.TryParse(value[(i + 1)..], out number);
    }

    private static VersionParts Parse(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return new VersionParts(new Version(0, 0, 0), string.Empty, true);
        }

        var dash = normalized.IndexOf('-', StringComparison.Ordinal);
        var coreText = dash >= 0 ? normalized[..dash] : normalized;
        var pre = dash >= 0 ? normalized[(dash + 1)..] : string.Empty;
        if (!Version.TryParse(coreText, out var core))
        {
            core = new Version(0, 0, 0);
            pre = normalized;
        }

        return new VersionParts(core, pre, string.IsNullOrWhiteSpace(pre));
    }

    private readonly record struct VersionParts(Version Core, string PreRelease, bool IsRelease);
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Clocteck.CubicCenter.Services;

public static partial class HardwareNameFormatter
{
    public static string FormatCpu(string? value)
    {
        var text = Normalize(value, "CPU");

        var ryzenAi = RyzenAiRegex().Match(text);
        if (ryzenAi.Success)
        {
            var family = ryzenAi.Groups[2].Success ? ryzenAi.Groups[2].Value.Trim() : string.Empty;
            var model = string.IsNullOrEmpty(family)
                ? ryzenAi.Groups[3].Value
                : $"{family} {ryzenAi.Groups[3].Value}";
            return Limit($"AI{ryzenAi.Groups[1].Value} {model}", 9);
        }

        var ryzen = RyzenRegex().Match(text);
        if (ryzen.Success) return Limit($"R{ryzen.Groups[1].Value} {ryzen.Groups[2].Value}", 9);

        var ultra = CoreUltraRegex().Match(text);
        if (ultra.Success) return Limit($"U{ultra.Groups[1].Value} {ultra.Groups[2].Value}", 9);

        var intel = IntelModelRegex().Match(text);
        if (intel.Success) return Limit(intel.Value.Replace("Core ", string.Empty, StringComparison.OrdinalIgnoreCase), 9);

        text = VendorCpuWordsRegex().Replace(text, " ");
        return Limit(CollapseSpaces(text), 9);
    }

    public static string FormatGpu(string? value)
    {
        var text = Normalize(value, "GPU");

        var geforce = GeForceRegex().Match(text);
        if (geforce.Success)
        {
            var suffix = geforce.Groups[3].Value.Equals("SUPER", StringComparison.OrdinalIgnoreCase) ? "S" : geforce.Groups[3].Value;
            return Limit($"{geforce.Groups[1].Value.ToUpperInvariant()} {geforce.Groups[2].Value}{suffix}", 11);
        }

        var radeonRx = RadeonRxRegex().Match(text);
        if (radeonRx.Success) return Limit($"RX {radeonRx.Groups[1].Value}{radeonRx.Groups[2].Value}", 11);

        var radeon = RadeonRegex().Match(text);
        if (radeon.Success) return Limit($"Radeon {radeon.Groups[1].Value}", 11);

        var arc = ArcRegex().Match(text);
        if (arc.Success) return Limit($"Arc {arc.Groups[1].Value}", 11);

        text = VendorGpuWordsRegex().Replace(text, " ");
        return Limit(CollapseSpaces(text), 11);
    }

    private static string Normalize(string? value, string fallback) =>
        CollapseSpaces(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());

    private static string CollapseSpaces(string value) => SpaceRegex().Replace(value, " ").Trim();

    private static string Limit(string value, int maxCharacters)
    {
        var elements = StringInfo.ParseCombiningCharacters(value);
        if (elements.Length <= maxCharacters) return value;
        return value[..elements[maxCharacters]].TrimEnd();
    }

    [GeneratedRegex(@"Ryzen\s+AI\s+([3579])\s+(HX\s*)?(\d{3}[A-Z]*)", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenAiRegex();

    [GeneratedRegex(@"Ryzen\s+([3579])\s+(\d{4,5}[A-Z0-9]*)", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenRegex();

    [GeneratedRegex(@"Core\s+Ultra\s+([3579])\s+(\d{3}[A-Z0-9]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CoreUltraRegex();

    [GeneratedRegex(@"(?:Core\s+)?i[3579]-?\d{4,5}[A-Z0-9]*", RegexOptions.IgnoreCase)]
    private static partial Regex IntelModelRegex();

    [GeneratedRegex(@"\b(AMD|Intel|Processor|with|Radeon|Graphics|CPU|Series)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VendorCpuWordsRegex();

    [GeneratedRegex(@"\b(RTX|GTX)\s*(\d{3,4})\s*(Ti|SUPER)?", RegexOptions.IgnoreCase)]
    private static partial Regex GeForceRegex();

    [GeneratedRegex(@"\bRX\s*(\d{4})\s*(XTX|XT)?", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonRxRegex();

    [GeneratedRegex(@"Radeon\s+([0-9]{3,4}[A-Z]{0,2})", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonRegex();

    [GeneratedRegex(@"\bArc\s+([A-Z]\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex ArcRegex();

    [GeneratedRegex(@"\b(NVIDIA|GeForce|AMD|Intel|Graphics|Laptop|GPU)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VendorGpuWordsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}

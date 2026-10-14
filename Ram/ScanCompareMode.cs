namespace EcuRamTool.Ram;

public enum ScanCompareMode
{
    Exact,
    Approx,
    Below,
    Above
}

public static class ScanCompareModeText
{
    public const string ExactText = "Exact this value";
    public const string ApproxText = "Ungefähr dieser Value";
    public const string BelowText = "Unter diesem Value";
    public const string AboveText = "Über diesem Value";

    public static ScanCompareMode FromUiText(string? text)
    {
        return text switch
        {
            ApproxText => ScanCompareMode.Approx,
            BelowText => ScanCompareMode.Below,
            AboveText => ScanCompareMode.Above,
            _ => ScanCompareMode.Exact
        };
    }

    public static string ToUiText(ScanCompareMode mode)
    {
        return mode switch
        {
            ScanCompareMode.Approx => ApproxText,
            ScanCompareMode.Below => BelowText,
            ScanCompareMode.Above => AboveText,
            _ => ExactText
        };
    }
}

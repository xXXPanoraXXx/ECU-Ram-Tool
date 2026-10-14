namespace EcuRamTool.Ram;

public enum ScanValueType
{
    BitMask,
    UInt8,
    UInt16
}

public static class ScanValueTypeText
{
    public const string BitMaskText = "Bitmask (Statusbits/Flags)";
    public const string UInt8Text = "8 bit";
    public const string UInt16Text = "16 bit";

    public static ScanValueType FromUiText(string? text)
    {
        return text switch
        {
            UInt8Text => ScanValueType.UInt8,
            UInt16Text => ScanValueType.UInt16,
            _ => ScanValueType.BitMask
        };
    }

    public static string ToUiText(ScanValueType type)
    {
        return type switch
        {
            ScanValueType.UInt8 => UInt8Text,
            ScanValueType.UInt16 => UInt16Text,
            _ => BitMaskText
        };
    }
}

namespace EcuRamTool.Ram;

public sealed class ScanCandidate
{
    public int Address24 { get; set; }
    public ScanValueType ValueType { get; set; } = ScanValueType.BitMask;
    public int Bit { get; set; } = -1;
    public int RawValue { get; set; }
    public double Factor { get; set; } = 1.0;
    public string Name { get; set; } = "";
    public string ReadMethod { get; set; } = "";
    public string WriteMethod { get; set; } = "";

    public int ByteLength => ValueType == ScanValueType.UInt16 ? 2 : 1;
    public int Mask => Bit >= 0 ? 1 << Bit : 0;

    public string AddressText
    {
        get
        {
            string baseText = "0x" + Address24.ToString("X6");
            return ValueType == ScanValueType.BitMask && Bit >= 0 ? baseText + "." + Bit : baseText;
        }
    }

    public string TypeText => ScanValueTypeText.ToUiText(ValueType);

    public string ValueText
    {
        get
        {
            if (ValueType == ScanValueType.BitMask)
                return RawValue.ToString();

            double scaled = RawValue * Factor;
            if (Math.Abs(Factor - 1.0) < 0.000000001)
                return RawValue.ToString();

            return scaled.ToString("0.######") + "  (raw 0x" + RawValue.ToString(ValueType == ScanValueType.UInt16 ? "X4" : "X2") + ")";
        }
    }

    public string RawText => ValueType switch
    {
        ScanValueType.UInt16 => "0x" + RawValue.ToString("X4"),
        ScanValueType.UInt8 => "0x" + RawValue.ToString("X2"),
        _ => RawValue.ToString()
    };
}

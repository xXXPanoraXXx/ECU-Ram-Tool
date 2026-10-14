using System.Text.Json.Serialization;

namespace EcuRamTool.Ram;

public sealed class WatchItem
{
    public int Address24 { get; set; }
    public ScanValueType ValueType { get; set; } = ScanValueType.BitMask;

    // Für automatisch gefundene Statusbits: Bit 0..7.
    // Für manuell angelegte Masken kann Bit -1 bleiben und MaskValue genutzt werden.
    public int Bit { get; set; } = -1;

    // Optional für manuell angelegte Flags/Statusbits, z.B. 0x8000.
    // 0 bedeutet: alte Bit-Logik über Bit verwenden.
    public int MaskValue { get; set; }

    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int LastRawValue { get; set; }
    public double Factor { get; set; } = 1.0;
    public string ReadMethod { get; set; } = "";
    public string WriteMethod { get; set; } = "";

    // Runtime-only: Force-Write soll nicht automatisch aus einer Config geladen und gestartet werden.
    [JsonIgnore]
    public bool ForceWriteEnabled { get; set; }

    [JsonIgnore]
    public int ForceWriteRawValue { get; set; }

    [JsonIgnore]
    public int ForceWriteIntervalMs { get; set; } = 0;

    public int ByteLength
    {
        get
        {
            if (ValueType == ScanValueType.UInt16)
                return 2;

            if (ValueType == ScanValueType.BitMask && Mask > 0xFF)
                return 2;

            return 1;
        }
    }

    public int Mask => MaskValue > 0 ? MaskValue : Bit >= 0 ? 1 << Bit : 0;

    public string AddressText
    {
        get
        {
            string baseText = "0x" + Address24.ToString("X6");

            if (ValueType != ScanValueType.BitMask)
                return baseText;

            if (MaskValue > 0)
                return baseText + " & 0x" + MaskValue.ToString(MaskValue > 0xFF ? "X4" : "X2");

            return Bit >= 0 ? baseText + "." + Bit : baseText;
        }
    }

    public string TypeText => ScanValueTypeText.ToUiText(ValueType);

    public string UnitText => Unit ?? string.Empty;

    public string RawValueText
    {
        get
        {
            if (ValueType == ScanValueType.UInt16 || ByteLength >= 2)
                return "0x" + LastRawValue.ToString("X4");

            return "0x" + LastRawValue.ToString("X2");
        }
    }

    public string ValueText
    {
        get
        {
            if (ValueType == ScanValueType.BitMask)
            {
                int mask = Mask;
                if (mask <= 0)
                    return LastRawValue.ToString();

                if (LastRawValue == 0)
                    return "0";

                // Einzelnes Bit/Flag als 0/1 anzeigen, Multi-Bit-Maske als Hexwert.
                if (IsPowerOfTwo(mask))
                    return "1";

                return "0x" + LastRawValue.ToString(mask > 0xFF ? "X4" : "X2");
            }

            double scaled = LastRawValue * Factor;
            return scaled.ToString("0.######");
        }
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}

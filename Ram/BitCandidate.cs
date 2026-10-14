namespace EcuRamTool.Ram;

public sealed class BitCandidate
{
    public int Address24 { get; set; }
    public int Bit { get; set; }
    public int Value { get; set; }
    public string Name { get; set; } = "";

    public int Mask => 1 << Bit;
    public string AddressText => "0x" + Address24.ToString("X6");
    public string MaskText => "0x" + Mask.ToString("X2");
}

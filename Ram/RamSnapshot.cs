namespace EcuRamTool.Ram;

public sealed class RamSnapshot
{
    private readonly Dictionary<int, byte> _bytes = new();

    public IReadOnlyDictionary<int, byte> Bytes => _bytes;

    public void SetByte(int address24, byte value) => _bytes[address24 & 0xFFFFFF] = value;

    public bool TryGetByte(int address24, out byte value) => _bytes.TryGetValue(address24 & 0xFFFFFF, out value);

    public int Count => _bytes.Count;
}

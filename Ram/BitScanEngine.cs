namespace EcuRamTool.Ram;

public sealed class BitScanEngine
{
    private List<BitCandidate> _candidates = new();
    public IReadOnlyList<BitCandidate> Candidates => _candidates;

    public void FirstScan(RamSnapshot snapshot, int wantedBitValue)
    {
        _candidates = snapshot.Bytes
            .OrderBy(x => x.Key)
            .SelectMany(kv => BitsFromByte(kv.Key, kv.Value, wantedBitValue))
            .ToList();
    }

    public void NextScan(RamSnapshot snapshot, int wantedBitValue)
    {
        _candidates = _candidates
            .Where(c => snapshot.TryGetByte(c.Address24, out byte current) && (((current >> c.Bit) & 1) == wantedBitValue))
            .Select(c =>
            {
                c.Value = wantedBitValue;
                return c;
            })
            .ToList();
    }

    public void Clear() => _candidates.Clear();

    private static IEnumerable<BitCandidate> BitsFromByte(int address24, byte value, int wantedBitValue)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            int bitValue = (value >> bit) & 1;
            if (bitValue == wantedBitValue)
            {
                yield return new BitCandidate
                {
                    Address24 = address24,
                    Bit = bit,
                    Value = bitValue
                };
            }
        }
    }
}

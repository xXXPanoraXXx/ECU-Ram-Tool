namespace EcuRamTool.Ram;

public sealed class ValueScanEngine
{
    private List<ScanCandidate> _candidates = new();
    public IReadOnlyList<ScanCandidate> Candidates => _candidates;

    public RamSnapshot? PreviousSnapshot { get; private set; }
    public RamSnapshot? CurrentSnapshot { get; private set; }

    public void FirstScan(
        RamSnapshot snapshot,
        ScanValueType valueType,
        double wantedValue,
        double factor,
        ScanCompareMode compareMode,
        string? readMethod = null,
        string? writeMethod = null)
    {
        factor = NormalizeFactor(factor);
        compareMode = NormalizeCompareMode(valueType, compareMode);
        _candidates = BuildCandidates(snapshot, valueType, wantedValue, factor, compareMode, readMethod, writeMethod).ToList();
        PreviousSnapshot = null;
        CurrentSnapshot = snapshot;
    }

    public void NextScan(
        RamSnapshot snapshot,
        ScanValueType valueType,
        double wantedValue,
        double factor,
        ScanCompareMode compareMode)
    {
        factor = NormalizeFactor(factor);
        compareMode = NormalizeCompareMode(valueType, compareMode);

        var next = new List<ScanCandidate>();

        foreach (ScanCandidate candidate in _candidates)
        {
            if (candidate.ValueType != valueType)
                continue;

            if (!TryReadRaw(snapshot, candidate, out int raw))
                continue;

            int compareValue = valueType == ScanValueType.BitMask
                ? ExtractBit(raw, candidate.Bit)
                : raw;

            if (!Matches(valueType, compareValue, wantedValue, factor, compareMode))
                continue;

            candidate.RawValue = compareValue;
            candidate.Factor = factor;
            next.Add(candidate);
        }

        _candidates = next;
        PreviousSnapshot = CurrentSnapshot;
        CurrentSnapshot = snapshot;
    }

    public void Clear()
    {
        _candidates.Clear();
        PreviousSnapshot = null;
        CurrentSnapshot = null;
    }

    private static IEnumerable<ScanCandidate> BuildCandidates(
        RamSnapshot snapshot,
        ScanValueType valueType,
        double wantedValue,
        double factor,
        ScanCompareMode compareMode,
        string? readMethod,
        string? writeMethod)
    {
        var addresses = snapshot.Bytes.Keys.OrderBy(x => x).ToList();

        if (valueType == ScanValueType.BitMask)
        {
            int wantedBit = wantedValue == 0 ? 0 : 1;
            foreach (int address in addresses)
            {
                byte value = snapshot.Bytes[address];
                for (int bit = 0; bit < 8; bit++)
                {
                    int bitValue = (value >> bit) & 1;
                    if (bitValue == wantedBit)
                    {
                        yield return new ScanCandidate
                        {
                            Address24 = address,
                            ValueType = ScanValueType.BitMask,
                            Bit = bit,
                            RawValue = bitValue,
                            Factor = 1.0,
                            ReadMethod = readMethod ?? string.Empty,
                            WriteMethod = writeMethod ?? string.Empty
                        };
                    }
                }
            }

            yield break;
        }

        if (valueType == ScanValueType.UInt8)
        {
            foreach (int address in addresses)
            {
                int raw = snapshot.Bytes[address];
                if (Matches(valueType, raw, wantedValue, factor, compareMode))
                {
                    yield return new ScanCandidate
                    {
                        Address24 = address,
                        ValueType = ScanValueType.UInt8,
                        RawValue = raw,
                        Factor = factor,
                        ReadMethod = readMethod ?? string.Empty,
                        WriteMethod = writeMethod ?? string.Empty
                    };
                }
            }

            yield break;
        }

        if (valueType == ScanValueType.UInt16)
        {
            foreach (int address in addresses)
            {
                if (!snapshot.TryGetByte(address, out byte lo) || !snapshot.TryGetByte(address + 1, out byte hi))
                    continue;

                int raw = lo | (hi << 8); // C166/ST10 RAM ist für Words in der Praxis little endian im Dump/Read.
                if (Matches(valueType, raw, wantedValue, factor, compareMode))
                {
                    yield return new ScanCandidate
                    {
                        Address24 = address,
                        ValueType = ScanValueType.UInt16,
                        RawValue = raw,
                        Factor = factor,
                        ReadMethod = readMethod ?? string.Empty,
                        WriteMethod = writeMethod ?? string.Empty
                    };
                }
            }
        }
    }

    private static bool TryReadRaw(RamSnapshot snapshot, ScanCandidate candidate, out int raw)
    {
        raw = 0;

        if (candidate.ValueType == ScanValueType.BitMask || candidate.ValueType == ScanValueType.UInt8)
        {
            if (!snapshot.TryGetByte(candidate.Address24, out byte value))
                return false;
            raw = value;
            return true;
        }

        if (candidate.ValueType == ScanValueType.UInt16)
        {
            if (!snapshot.TryGetByte(candidate.Address24, out byte lo) || !snapshot.TryGetByte(candidate.Address24 + 1, out byte hi))
                return false;
            raw = lo | (hi << 8);
            return true;
        }

        return false;
    }

    private static bool Matches(ScanValueType type, int raw, double wantedValue, double factor, ScanCompareMode compareMode)
    {
        if (type == ScanValueType.BitMask)
            return (wantedValue == 0 ? 0 : 1) == raw;

        double normalizedFactor = NormalizeFactor(factor);
        double scaled = raw * normalizedFactor;

        return compareMode switch
        {
            ScanCompareMode.Below => scaled < wantedValue,
            ScanCompareMode.Above => scaled > wantedValue,
            ScanCompareMode.Approx => Math.Abs(scaled - wantedValue) <= GetApproxTolerance(wantedValue, normalizedFactor),
            _ => raw == (int)Math.Round(wantedValue / normalizedFactor, MidpointRounding.AwayFromZero)
        };
    }

    private static ScanCompareMode NormalizeCompareMode(ScanValueType type, ScanCompareMode compareMode)
    {
        return type == ScanValueType.BitMask ? ScanCompareMode.Exact : compareMode;
    }

    private static double GetApproxTolerance(double wantedValue, double factor)
    {
        // Cheat-Engine-artig bewusst tolerant, aber nicht extrem:
        // ±2% vom gesuchten Wert, mindestens aber ein Rohwert-Schritt nach Faktor.
        double oneRawStep = Math.Abs(NormalizeFactor(factor));
        double percentWindow = Math.Abs(wantedValue) * 0.02;
        return Math.Max(oneRawStep, percentWindow);
    }

    private static int ExtractBit(int rawByte, int bit)
    {
        if (bit < 0 || bit > 7)
            return 0;
        return (rawByte >> bit) & 1;
    }

    private static double NormalizeFactor(double factor)
    {
        return factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor) ? 1.0 : factor;
    }
}

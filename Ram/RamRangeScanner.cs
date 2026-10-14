using EcuRamTool.Kwp;
using EcuRamTool.Profiles;

namespace EcuRamTool.Ram;

public sealed class RamRangeScanner
{
    private readonly KwpSession _session;
    private readonly Action<string> _log;

    public RamRangeScanner(KwpSession session, Action<string> log)
    {
        _session = session;
        _log = log;
    }

    public RamSnapshot ReadRange(MemoryRangeProfile range, CancellationToken token = default)
    {
        int prefix = HexUtil.ParseInt(range.AddressPrefix) & 0xFF;
        int start16 = HexUtil.ParseInt(range.Start) & 0xFFFF;
        int end16 = HexUtil.ParseInt(range.End) & 0xFFFF;
        int readSize = Math.Max(1, Math.Min(0x3F, range.ReadSize));

        if (end16 < start16)
            throw new InvalidOperationException("MemoryRange End ist kleiner als Start.");

        var snapshot = new RamSnapshot();
        int totalBytes = end16 - start16 + 1;
        int done = 0;

        for (int offset = start16; offset <= end16; offset += readSize)
        {
            token.ThrowIfCancellationRequested();

            int remaining = end16 - offset + 1;
            int len = Math.Min(readSize, remaining);
            int address24 = (prefix << 16) | offset;

            string? readMethod = range.ReadMethod ?? _session.Profile?.RamReadMethod;
            byte[] payload = _session.ReadMemory(address24, len, readMethod);
            for (int i = 0; i < payload.Length; i++)
                snapshot.SetByte(address24 + i, payload[i]);

            done += payload.Length;
            if (done % 32 == 0 || done >= totalBytes)
                _log($"Read {range.Name}: {done}/{totalBytes} bytes");
        }

        return snapshot;
    }
}

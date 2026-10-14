namespace EcuRamTool.Kwp;

public sealed class KwpCommandResult
{
    public byte[] Tx { get; init; } = Array.Empty<byte>();
    public byte[] Echo { get; init; } = Array.Empty<byte>();
    public byte[] Rx { get; init; } = Array.Empty<byte>();
    public bool HasResponse => Rx.Length > 0;
}

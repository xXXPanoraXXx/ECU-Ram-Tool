namespace EcuRamTool.Kwp;

public static class KwpChecksum
{
    public static byte Calculate(IEnumerable<byte> bytesWithoutChecksum)
    {
        int sum = 0;
        foreach (byte b in bytesWithoutChecksum)
            sum = (sum + b) & 0xFF;
        return (byte)sum;
    }
}

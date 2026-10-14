using System.Globalization;
using System.Text;

namespace EcuRamTool.Kwp;

public static class HexUtil
{
    public static byte ParseByte(string value)
    {
        value = CleanHex(value);
        return byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static int ParseInt(string value)
    {
        value = CleanHex(value);
        return int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static byte[] ParseBytes(string text)
    {
        string cleaned = text
            .Replace(",", " ")
            .Replace(";", " ")
            .Replace("-", " ")
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            return Array.Empty<byte>();

        string[] parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 2)
                throw new FormatException("Ungültiges Hex-Byte: " + parts[i]);
            bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }

    public static string Format(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

    public static string FormatAddress(int address24) => "0x" + address24.ToString("X6");

    public static bool ContainsSequence(byte[] data, byte[] sequence)
    {
        if (sequence.Length == 0 || data.Length < sequence.Length)
            return false;
        for (int i = 0; i <= data.Length - sequence.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (data[i + j] != sequence[j])
                {
                    ok = false;
                    break;
                }
            }
            if (ok) return true;
        }
        return false;
    }

    public static bool ContainsAnySequence(byte[] data, IEnumerable<string> sequences)
    {
        foreach (string sequenceText in sequences)
        {
            byte[] seq = ParseBytes(sequenceText);
            if (ContainsSequence(data, seq))
                return true;
        }
        return false;
    }

    private static string CleanHex(string value)
    {
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];
        return value;
    }
}

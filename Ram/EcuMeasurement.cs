using System.Globalization;
using System.Text;

namespace EcuRamTool.Ram;

public sealed class EcuMeasurement
{
    public bool Selected { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int Address24 { get; set; }
    public int Size { get; set; }
    public int Bitmask { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool Signed { get; set; }
    public bool Inverse { get; set; }
    public double Factor { get; set; } = 1.0;
    public double Offset { get; set; }
    public string Comment { get; set; } = string.Empty;

    public string AddressText => "0x" + Address24.ToString("X6");
    public string TypeText => Bitmask > 0 ? "Bitmask/Flags" : Size == 2 ? "16 bit" : "8 bit";
    public string AliasText => string.IsNullOrWhiteSpace(Alias) ? string.Empty : Alias;

    public WatchItem ToWatchItem(string readMethod = "", string writeMethod = "")
    {
        ScanValueType valueType = Bitmask > 0
            ? ScanValueType.BitMask
            : Size == 2 ? ScanValueType.UInt16 : ScanValueType.UInt8;

        double factor = Bitmask > 0 ? 1.0 : Factor;
        if (Math.Abs(factor) < 0.000000001 || double.IsNaN(factor) || double.IsInfinity(factor))
            factor = 1.0;

        return new WatchItem
        {
            Address24 = Address24,
            ValueType = valueType,
            Bit = TryGetSingleBitIndex(Bitmask),
            MaskValue = Bitmask,
            Name = Name,
            Unit = Unit,
            Factor = factor,
            ReadMethod = readMethod,
            WriteMethod = writeMethod
        };
    }

    private static int TryGetSingleBitIndex(int mask)
    {
        if (mask <= 0 || (mask & (mask - 1)) != 0)
            return -1;

        int bit = 0;
        while (((mask >> bit) & 1) == 0)
            bit++;
        return bit;
    }
}

public static class EcuMeasurementParser
{
    public static List<EcuMeasurement> LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(".ecu-Datei wurde nicht gefunden.", path);

        var result = new List<EcuMeasurement>();
        bool inMeasurements = false;

        foreach (string rawLine in File.ReadLines(path, Encoding.Latin1))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                inMeasurements = string.Equals(line, "[Measurements]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inMeasurements || line.StartsWith(";", StringComparison.Ordinal))
                continue;

            List<string> fields = SplitEcuCsv(line);
            if (fields.Count < 11)
                continue;

            try
            {
                string name = fields[0].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int address = ParseNumber(fields[2]);
                int size = ParseNumber(fields[3]);
                int bitmask = ParseNumber(fields[4]);

                // Die Watchlist unterstützt aktuell 8-bit, 16-bit und Flags.
                // Größere .ecu-Variablen werden bewusst ausgelassen, bis wir dafür einen eigenen Typ haben.
                if (size != 1 && size != 2)
                    continue;

                result.Add(new EcuMeasurement
                {
                    Name = name,
                    Alias = UnwrapBraces(fields[1]),
                    Address24 = address,
                    Size = size,
                    Bitmask = bitmask,
                    Unit = UnwrapBraces(fields[5]),
                    Signed = ParseNumber(fields[6]) != 0,
                    Inverse = ParseNumber(fields[7]) != 0,
                    Factor = ParseDouble(fields[8], 1.0),
                    Offset = ParseDouble(fields[9], 0.0),
                    Comment = UnwrapBraces(string.Join(",", fields.Skip(10)))
                });
            }
            catch
            {
                // Einzelne kaputte Zeilen ignorieren. Die .ecu-Dateien sind oft manuell editiert.
            }
        }

        return result;
    }

    private static List<string> SplitEcuCsv(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        int braceDepth = 0;

        foreach (char ch in line)
        {
            if (ch == '{')
                braceDepth++;
            else if (ch == '}' && braceDepth > 0)
                braceDepth--;

            if (ch == ',' && braceDepth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static string UnwrapBraces(string text)
    {
        string value = text.Trim();
        if (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal) && value.Length >= 2)
            value = value.Substring(1, value.Length - 2);
        return value.Trim();
    }

    private static int ParseNumber(string text)
    {
        string value = UnwrapBraces(text).Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value.Substring(2), 16);

        if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value.Substring(0, value.Length - 1), 16);

        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static double ParseDouble(string text, double fallback)
    {
        string value = UnwrapBraces(text).Trim().Replace(',', '.');
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return fallback;
    }
}

public static class EcuMeasurementWriter
{
    public static string AppendWatchItem(string path, WatchItem item)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Kein .ecu Pfad angegeben.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException(".ecu-Datei wurde nicht gefunden.", path);

        List<EcuMeasurement> existing = EcuMeasurementParser.LoadFromFile(path);
        int size = item.ByteLength <= 1 ? 1 : 2;
        int bitmask = item.ValueType == ScanValueType.BitMask ? Math.Max(0, item.Mask) : 0;

        bool alreadyExists = existing.Any(x =>
            x.Address24 == item.Address24 &&
            x.Size == size &&
            x.Bitmask == bitmask);

        if (alreadyExists)
            throw new InvalidOperationException("Diese Adresse/Maske ist in der ausgewählten .ecu bereits vorhanden.");

        string baseName = MakeSafeName(item.Name, item.Address24, bitmask);
        string name = MakeUniqueName(baseName, existing.Select(x => x.Name));
        string unit = item.ValueType == ScanValueType.BitMask ? string.Empty : item.Unit ?? string.Empty;
        double factor = item.ValueType == ScanValueType.BitMask ? 1.0 : NormalizeFactor(item.Factor);
        string comment = "Added by EcuRamTool";

        string line = FormatMeasurementLine(
            name,
            alias: string.Empty,
            address24: item.Address24,
            size: size,
            bitmask: bitmask,
            unit: unit,
            signed: false,
            inverse: false,
            factor: factor,
            offset: 0.0,
            comment: comment);

        EnsureMeasurementsSection(path);
        AppendLineLatin1(path, line);
        return name;
    }

    private static void EnsureMeasurementsSection(string path)
    {
        bool hasMeasurements = File.ReadLines(path, Encoding.Latin1)
            .Any(x => string.Equals(x.Trim(), "[Measurements]", StringComparison.OrdinalIgnoreCase));

        if (hasMeasurements)
            return;

        AppendLineLatin1(path, string.Empty);
        AppendLineLatin1(path, "[Measurements]");
        AppendLineLatin1(path, ";Name           , {Alias}                           , Address, Size, Bitmask, {Unit},    S, I,            A,      B, Comment");
    }

    private static void AppendLineLatin1(string path, string line)
    {
        string prefix = string.Empty;
        var info = new FileInfo(path);
        if (info.Length > 0)
        {
            string text = File.ReadAllText(path, Encoding.Latin1);
            if (!text.EndsWith("\n", StringComparison.Ordinal))
                prefix = Environment.NewLine;
        }

        File.AppendAllText(path, prefix + line + Environment.NewLine, Encoding.Latin1);
    }

    private static string FormatMeasurementLine(
        string name,
        string alias,
        int address24,
        int size,
        int bitmask,
        string unit,
        bool signed,
        bool inverse,
        double factor,
        double offset,
        string comment)
    {
        string factorText = factor.ToString("0.########", CultureInfo.InvariantCulture);
        string offsetText = offset.ToString("0.########", CultureInfo.InvariantCulture);

        return string.Format(CultureInfo.InvariantCulture,
            "{0,-16}, {{{1}}}, 0x{2:X6}, {3,2}, 0x{4:X4}, {{{5}}}, {6}, {7}, {8,12}, {9,6}, {{{10}}}",
            name,
            EscapeBraced(alias),
            address24,
            size,
            bitmask,
            EscapeBraced(unit),
            signed ? 1 : 0,
            inverse ? 1 : 0,
            factorText,
            offsetText,
            EscapeBraced(comment));
    }

    private static string MakeSafeName(string? requestedName, int address24, int bitmask)
    {
        string name = string.IsNullOrWhiteSpace(requestedName)
            ? "ram_" + address24.ToString("X6")
            : requestedName.Trim();

        var chars = new StringBuilder(name.Length + 8);
        foreach (char ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                chars.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '.')
                chars.Append('_');
        }

        name = chars.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(name))
            name = "ram_" + address24.ToString("X6");

        if (char.IsDigit(name[0]))
            name = "ram_" + name;

        if (bitmask > 0 && !name.Contains("_b", StringComparison.OrdinalIgnoreCase))
            name += "_b" + bitmask.ToString(bitmask > 0xFF ? "X4" : "X2");

        return name;
    }

    private static string MakeUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;

        for (int i = 2; i < 10000; i++)
        {
            string candidate = baseName + "_" + i.ToString(CultureInfo.InvariantCulture);
            if (!names.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Konnte keinen eindeutigen .ecu Namen erzeugen.");
    }

    private static double NormalizeFactor(double factor)
    {
        if (Math.Abs(factor) < 0.000000001 || double.IsNaN(factor) || double.IsInfinity(factor))
            return 1.0;
        return factor;
    }

    private static string EscapeBraced(string? text)
    {
        return (text ?? string.Empty)
            .Replace("{", "(", StringComparison.Ordinal)
            .Replace("}", ")", StringComparison.Ordinal)
            .Trim();
    }
}

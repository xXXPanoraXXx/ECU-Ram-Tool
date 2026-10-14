using EcuRamTool.Profiles;

namespace EcuRamTool.Kwp;

public sealed class KwpFrameBuilder
{
    private readonly EcuProfile _profile;

    public KwpFrameBuilder(EcuProfile profile)
    {
        _profile = profile;
    }

    public byte[] BuildStartCommunication(byte target) => Build(_profile.Commands.StartCommunication, target, null, 0);
    public byte[] BuildTesterPresent(byte target) => Build(_profile.Commands.TesterPresent, target, null, 0);
    public byte[] BuildDevelopmentSession(byte target) => Build(_profile.Commands.DevelopmentSession, target, null, 0);

    public byte[] BuildReadMemory(byte target, int address24, int length)
    {
        return Build(_profile.Commands.ReadMemoryByAddress, target, address24, length);
    }

    private byte[] Build(string template, byte target, int? address24, int length)
    {
        byte tester = HexUtil.ParseByte(_profile.TesterAddress);

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{target}"] = target.ToString("X2"),
            ["{tester}"] = tester.ToString("X2"),
            ["{len}"] = ((byte)length).ToString("X2")
        };

        if (address24.HasValue)
        {
            int addr = address24.Value & 0xFFFFFF;
            replacements["{addr3}"] = ((addr >> 16) & 0xFF).ToString("X2");
            replacements["{addr2}"] = ((addr >> 8) & 0xFF).ToString("X2");
            replacements["{addr1}"] = (addr & 0xFF).ToString("X2");
        }

        string withoutChecksum = template;
        foreach ((string key, string value) in replacements)
            withoutChecksum = withoutChecksum.Replace(key, value, StringComparison.OrdinalIgnoreCase);

        withoutChecksum = withoutChecksum.Replace("{checksum}", "", StringComparison.OrdinalIgnoreCase).Trim();
        byte[] body = HexUtil.ParseBytes(withoutChecksum);
        byte checksum = KwpChecksum.Calculate(body);
        return body.Concat(new[] { checksum }).ToArray();
    }
}

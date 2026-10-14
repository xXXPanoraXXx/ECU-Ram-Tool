using System.Text.Json.Serialization;

namespace EcuRamTool.Profiles;

public sealed class EcuProfile
{
    public string Name { get; set; } = "Unnamed ECU";
    public int BaudRate { get; set; } = 10400;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public string StopBits { get; set; } = "One";

    public List<string> TargetAddresses { get; set; } = new() { "0x10", "0x11", "0x01" };
    public string TesterAddress { get; set; } = "0xF1";

    // Globaler RAM-Lesepfad. Erlaubt z.B. "23" oder "DDLI21"/"2C21".
    // Einzelne MemoryRanges können das über ReadMethod überschreiben.
    public string RamReadMethod { get; set; } = "23";

    // Globaler RAM-Schreibpfad. Erlaubt z.B. "3D" oder "DDLI3B"/"2C3B".
    // Einzelne MemoryRanges/WatchItems können das über WriteMethod überschreiben.
    public string RamWriteMethod { get; set; } = "DDLI3B";

    public InitProfile Init { get; set; } = new();
    public TimingProfile Timing { get; set; } = new();
    public CommandTemplates Commands { get; set; } = new();
    public DdliProfile Ddli { get; set; } = new();
    public List<MemoryRangeProfile> MemoryRanges { get; set; } = new();
}

public sealed class InitProfile
{
    public string Mode { get; set; } = "FAST";
    public int PreOpenDelayMs { get; set; } = 200;
    public int FastInitHighBeforeMs { get; set; } = 2600;
    public double FastInitLowMs { get; set; } = 23;
    public double FastInitHighAfterMs { get; set; } = 26;
    public int AfterConnectDelayMs { get; set; } = 0;

    // Wichtig für K-Line: wenn mehrere Target-IDs getestet werden, bekommt jede ID
    // optional einen komplett neuen Port/Open + FastInit-Zyklus.
    public bool ReinitializeForEachTarget { get; set; } = true;
    public int DelayBetweenTargetAttemptsMs { get; set; } = 300;
}

public sealed class TimingProfile
{
    public int EchoTimeoutMs { get; set; } = 500;

    // Eigener Timeout nur für StartCommunication. Manche ECUs antworten hier langsamer
    // als bei normalen KWP-Kommandos.
    public int StartCommunicationTimeoutMs { get; set; } = 6000;

    public int DefaultResponseTimeoutMs { get; set; } = 3000;
    public int ReadMemoryTimeoutMs { get; set; } = 10000;
    public int KeepAliveIntervalMs { get; set; } = 1800;

    // Watchlist wird nur automatisch aktualisiert, wenn gerade kein anderer RAM-Read/Request aktiv ist.
    public int WatchRefreshIntervalMs { get; set; } = 750;

    // Sobald nach dem letzten empfangenen Byte so lange Ruhe ist, gilt RX als fertig.
    // Dadurch wird nicht stumpf der komplette Timeout abgewartet.
    public int InterByteIdleMs { get; set; } = 20;

    public int SerialWriteTimeoutMs { get; set; } = 1000;
    public int SerialPollSleepMs { get; set; } = 1;
}

public sealed class CommandTemplates
{
    public string StartCommunication { get; set; } = "81 {target} {tester} 81 {checksum}";
    public string TesterPresent { get; set; } = "81 {target} {tester} 3E {checksum}";
    public string DevelopmentSession { get; set; } = "82 {target} {tester} 10 86 {checksum}";
    public string ReadMemoryByAddress { get; set; } = "85 {target} {tester} 23 {addr3} {addr2} {addr1} {len} {checksum}";
    public string WriteMemoryByAddress { get; set; } = "{auto}";

    public List<string> PositiveStartContains { get; set; } = new() { "C1" };
    public List<string> PositiveDevelopmentContains { get; set; } = new() { "50 86" };
    public List<string> PositiveTesterPresentContains { get; set; } = new() { "7E" };
    public List<string> PositiveReadMemoryContains { get; set; } = new() { "63" };
    public List<string> PositiveWriteMemoryContains { get; set; } = new() { "7D" };
}

public sealed class DdliProfile
{
    public string LocalIdentifier { get; set; } = "0xF1";
    public int CommandPauseMs { get; set; } = 30;
    public bool FillFailedBlocks { get; set; } = true;
    public string FailedFillByte { get; set; } = "0xFF";
}

public sealed class MemoryRangeProfile
{
    public string Name { get; set; } = "FDxx Statusbits";
    public string AddressPrefix { get; set; } = "0x38";
    public string Start { get; set; } = "0xFD00";
    public string End { get; set; } = "0xFDFF";
    public int ReadSize { get; set; } = 2;

    // Optionaler Override pro Range. Leer/null = globales RamReadMethod aus dem Profil.
    // Unterstützt: "23" oder "DDLI21"/"2C21".
    public string? ReadMethod { get; set; }

    // Optionaler Override pro Range. Leer/null = globales RamWriteMethod aus dem Profil.
    // Unterstützt: "3D" oder "DDLI3B"/"2C3B".
    public string? WriteMethod { get; set; }

    public string Mode { get; set; } = "bits";
}

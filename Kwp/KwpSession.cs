using EcuRamTool.Profiles;

namespace EcuRamTool.Kwp;

public sealed class KwpSession : IDisposable
{
    private readonly Action<string> _log;
    private readonly object _ioLock = new();
    private readonly KwpSerialTransport _transport;
    private EcuProfile? _profile;
    private KwpFrameBuilder? _builder;
    private CancellationTokenSource? _keepAliveCts;
    private volatile bool _busy;
    private int? _ddliFastWriteAddress;
    private int _ddliFastWriteLength;
    private byte? _ddliFastWriteLocalIdentifier;

    public bool IsOpen => _transport.IsOpen;
    public bool IsBusy => _busy;
    public byte? ActiveTarget { get; private set; }
    public EcuProfile? Profile => _profile;

    public event Action<bool>? SessionStatusChanged;

    public KwpSession(Action<string> log)
    {
        _log = log;
        _transport = new KwpSerialTransport(log);
    }

    public bool Connect(string portName, EcuProfile profile)
    {
        _profile = profile;
        _builder = new KwpFrameBuilder(profile);
        ActiveTarget = null;
        InvalidateDdliFastWriteDefinition();
        StopKeepAlive();
        _transport.Close();

        if (!profile.Init.Mode.Equals("FAST", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("In diesem MVP ist nur FAST Init implementiert. Slow Init kann später als InitStrategy ergänzt werden.");

        for (int i = 0; i < profile.TargetAddresses.Count; i++)
        {
            string targetText = profile.TargetAddresses[i];
            byte target = HexUtil.ParseByte(targetText);

            try
            {
                if (!_transport.IsOpen || profile.Init.ReinitializeForEachTarget)
                {
                    _transport.Close();
                    _log($"Opening port and FAST init for target 0x{target:X2}...");
                    _transport.Open(portName, profile);
                    _transport.FastInit(profile);
                }

                byte[] start = _builder.BuildStartCommunication(target);
                _log($"Try target 0x{target:X2}: TX StartCommunication {HexUtil.Format(start)}");

                KwpCommandResult result = SendRawLocked(start, profile.Timing.StartCommunicationTimeoutMs, log: true);
                if (IsPositive(result.Rx, profile.Commands.PositiveStartContains))
                {
                    ActiveTarget = target;
                    _log($"Target 0x{target:X2} accepted.");
                    if (profile.Init.AfterConnectDelayMs > 0)
                        Thread.Sleep(profile.Init.AfterConnectDelayMs);

                    // Direkt nach erfolgreichem StartCommunication automatisch in die Dev-Session wechseln.
                    // Der genaue Befehl kommt weiterhin aus dem JSON-Profil.
                    try
                    {
                        StartDevelopmentSession();
                    }
                    catch (Exception ex)
                    {
                        _log("Development Session auto-start error: " + ex.Message);
                    }

                    StartKeepAlive();
                    SessionStatusChanged?.Invoke(true);
                    return true;
                }

                _log($"Target 0x{target:X2} not accepted. RX was: {HexUtil.Format(result.Rx)}");
            }
            catch (Exception ex)
            {
                _log($"Target 0x{target:X2} connect attempt error: {ex.Message}");
            }

            if (profile.Init.ReinitializeForEachTarget)
                _transport.Close();

            if (i < profile.TargetAddresses.Count - 1 && profile.Init.DelayBetweenTargetAttemptsMs > 0)
                Thread.Sleep(profile.Init.DelayBetweenTargetAttemptsMs);
        }

        _transport.Close();
        SessionStatusChanged?.Invoke(false);
        return false;
    }

    public KwpCommandResult SendUserCommand(byte[] tx, int? timeoutMs = null)
    {
        EnsureProfile();
        // Ein manueller Befehl kann theoretisch den DDLI LocalIdentifier verändern.
        // Deshalb den schnellen Force-Write-Cache danach nicht mehr blind benutzen.
        InvalidateDdliFastWriteDefinition();
        return SendRawLocked(tx, timeoutMs ?? _profile!.Timing.DefaultResponseTimeoutMs, log: true);
    }

    public bool StartDevelopmentSession()
    {
        EnsureReady();
        byte[] tx = _builder!.BuildDevelopmentSession(ActiveTarget!.Value);
        KwpCommandResult result = SendRawLocked(tx, _profile!.Timing.DefaultResponseTimeoutMs, log: true);
        bool ok = IsPositive(result.Rx, _profile.Commands.PositiveDevelopmentContains);
        _log(ok ? "Development Session confirmed." : "Development Session not confirmed.");
        return ok;
    }

    public byte[] ReadMemory(int address24, int length)
    {
        return ReadMemory(address24, length, null);
    }

    public byte[] ReadMemory(int address24, int length, string? readMethod)
    {
        EnsureReady();

        if (length <= 0)
            return Array.Empty<byte>();

        string method = NormalizeReadMethod(string.IsNullOrWhiteSpace(readMethod) ? _profile!.RamReadMethod : readMethod);

        if (method == "DDLI21")
        {
            byte li = HexUtil.ParseByte(_profile!.Ddli.LocalIdentifier);
            return ReadRamByDdli21((uint)address24, (uint)(address24 + length - 1), (byte)length, li);
        }

        byte[] tx = _builder!.BuildReadMemory(ActiveTarget!.Value, address24, length);
        KwpCommandResult result = SendRawLocked(tx, _profile!.Timing.ReadMemoryTimeoutMs, log: false);
        if (!result.HasResponse)
            throw new TimeoutException("Keine Antwort auf ReadMemory " + HexUtil.FormatAddress(address24));
        return ExtractReadMemoryPayload(result.Rx, length);
    }

    /// <summary>
    /// RAM lesen über Bosch/VAG DDLI: $2C LocalIdentifier definieren und anschließend per $21 lesen.
    /// Diese Methode entfernt die vorhandene $23-Logik nicht, sondern ist ein zusätzlicher Read-Pfad.
    /// </summary>
    public byte[] ReadRamByDdli21(
        uint startAddress,
        uint endAddress,
        byte blockSize = 0x20,
        byte localIdentifier = 0xF1)
    {
        EnsureReady();

        if (blockSize == 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize), "blockSize darf nicht 0 sein.");

        if (endAddress < startAddress)
            throw new ArgumentException("endAddress muss >= startAddress sein.");

        if (endAddress > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(endAddress), "DDLI nutzt hier 24-bit Adressen, max. 0xFFFFFF.");

        byte target = ActiveTarget!.Value;
        byte source = HexUtil.ParseByte(_profile!.TesterAddress);
        int timeoutMs = _profile.Timing.ReadMemoryTimeoutMs > 0
            ? _profile.Timing.ReadMemoryTimeoutMs
            : _profile.Timing.DefaultResponseTimeoutMs;
        int pauseMs = Math.Max(0, _profile.Ddli.CommandPauseMs);
        byte failedFill = HexUtil.ParseByte(_profile.Ddli.FailedFillByte);
        bool fillFailedBlocks = _profile.Ddli.FillFailedBlocks;

        var result = new List<byte>((int)(endAddress - startAddress + 1));

        _log($"DDLI21 RAM read start: 0x{startAddress:X6}-0x{endAddress:X6}, blockSize 0x{blockSize:X2}, LI 0x{localIdentifier:X2}, target 0x{target:X2}");

        // DDLI21 definiert denselben LocalIdentifier neu und macht damit eine evtl.
        // vorhandene schnelle Force-Write-Definition ungültig.
        InvalidateDdliFastWriteDefinition();

        lock (_ioLock)
        {
            _busy = true;

            try
            {
                for (uint address = startAddress; address <= endAddress;)
                {
                    int remaining = (int)(endAddress - address + 1);
                    byte currentBlockSize = (byte)Math.Min(blockSize, remaining);

                    bool blockOk = false;

                    try
                    {
                        // 1. DDLI LocalIdentifier löschen: 2C LI 04
                        byte[] clearFrame = BuildKwpFrame(target, source, 0x2C, localIdentifier, 0x04);
                        KwpCommandResult clearResult = SendRawInsideLock(clearFrame, timeoutMs, log: false);

                        if (!clearResult.HasResponse)
                        {
                            _log($"DDLI clear timeout at 0x{address:X6}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        if (!IsPositive2CResponse(clearResult.Rx, localIdentifier))
                        {
                            _log($"DDLI clear failed at 0x{address:X6}. RX: {HexUtil.Format(clearResult.Rx)}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        if (pauseMs > 0)
                            Thread.Sleep(pauseMs);

                        // 2. DDLI definieren:
                        // 2C LI 03 01 LEN AddrHigh AddrMid AddrLow
                        byte addrHigh = (byte)((address >> 16) & 0xFF);
                        byte addrMid = (byte)((address >> 8) & 0xFF);
                        byte addrLow = (byte)(address & 0xFF);

                        byte[] defineFrame = BuildKwpFrame(
                            target,
                            source,
                            0x2C,
                            localIdentifier,
                            0x03,
                            0x01,
                            currentBlockSize,
                            addrHigh,
                            addrMid,
                            addrLow);

                        KwpCommandResult defineResult = SendRawInsideLock(defineFrame, timeoutMs, log: false);

                        if (!defineResult.HasResponse)
                        {
                            _log($"DDLI define timeout at 0x{address:X6}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        if (!IsPositive2CResponse(defineResult.Rx, localIdentifier))
                        {
                            _log($"DDLI define failed at 0x{address:X6}. RX: {HexUtil.Format(defineResult.Rx)}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        if (pauseMs > 0)
                            Thread.Sleep(pauseMs);

                        // 3. DDLI lesen: 21 LI
                        byte[] readFrame = BuildKwpFrame(target, source, 0x21, localIdentifier);
                        KwpCommandResult readResult = SendRawInsideLock(readFrame, timeoutMs, log: false);

                        if (!readResult.HasResponse)
                        {
                            _log($"DDLI read timeout at 0x{address:X6}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        if (!TryExtract21Data(readResult.Rx, localIdentifier, currentBlockSize, out byte[] data))
                        {
                            _log($"DDLI read failed/extract failed at 0x{address:X6}. RX: {HexUtil.Format(readResult.Rx)}");
                            AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                            address += currentBlockSize;
                            continue;
                        }

                        result.AddRange(data.Take(currentBlockSize));
                        blockOk = true;
                    }
                    catch (Exception ex)
                    {
                        _log($"DDLI block exception at 0x{address:X6}: {ex.Message}");
                        AppendFailedBlock(result, currentBlockSize, fillFailedBlocks, failedFill);
                    }

                    if (!blockOk)
                        _log($"DDLI block marked failed: 0x{address:X6}, len 0x{currentBlockSize:X2}");

                    address += currentBlockSize;

                    // Überlauf-Schutz, falls endAddress nahe 0xFFFFFFFF wäre.
                    if (address == 0)
                        break;
                }
            }
            finally
            {
                _busy = false;
            }
        }

        _log($"DDLI21 RAM read done. Bytes: {result.Count}");
        return result.ToArray();
    }

    public bool CheckSessionAliveQuiet()
    {
        if (!IsOpen || ActiveTarget is null || _profile is null || _builder is null)
            return false;
        if (_busy)
            return true;

        try
        {
            byte[] tx = _builder.BuildTesterPresent(ActiveTarget.Value);
            KwpCommandResult result = SendRawLocked(tx, _profile.Timing.DefaultResponseTimeoutMs, log: false);

            // Auch beim TesterPresent gilt: jede echte ECU-Antwort bedeutet Verbindung lebt.
            // Eine negative Response wäre immer noch eine Antwort der ECU und damit kein K-Line-Verlust.
            bool alive = IsRealEcuResponseToTester(tx, result.Rx);
            SessionStatusChanged?.Invoke(alive);

            if (!alive)
                _log("Session lost: no ECU response to TesterPresent.");

            return alive;
        }
        catch
        {
            SessionStatusChanged?.Invoke(false);
            return false;
        }
    }

    private KwpCommandResult SendRawLocked(byte[] tx, int timeoutMs, bool log)
    {
        EnsureProfile();
        lock (_ioLock)
        {
            _busy = true;
            try
            {
                return SendRawInsideLock(tx, timeoutMs, log);
            }
            finally
            {
                _busy = false;
            }
        }
    }

    private KwpCommandResult SendRawInsideLock(byte[] tx, int timeoutMs, bool log)
    {
        EnsureProfile();

        if (log) _log("TX: " + HexUtil.Format(tx));
        _transport.Tx(tx);
        byte[] echo = _transport.ReadExact(tx.Length, _profile!.Timing.EchoTimeoutMs, _profile.Timing.SerialPollSleepMs);
        if (log) _log("RX echo: " + HexUtil.Format(echo));

        byte[] rx = _transport.RxUntilIdleOrTimeout(timeoutMs, _profile.Timing.InterByteIdleMs, _profile.Timing.SerialPollSleepMs);
        if (log) _log("RX: " + HexUtil.Format(rx));

        // Status nicht nur über TesterPresent aktualisieren:
        // Jede echte ECU-Antwort Richtung Tester bedeutet: Verbindung lebt.
        // Keine echte Antwort im Antwortfenster bedeutet: aktuell keine Verbindung/keine Antwort.
        // Wichtig: Echo zählt nicht als ECU-Antwort.
        UpdateStatusFromResponse(tx, rx);

        return new KwpCommandResult { Tx = tx, Echo = echo, Rx = rx };
    }

    private void UpdateStatusFromResponse(byte[] tx, byte[] rx)
    {
        bool alive = IsRealEcuResponseToTester(tx, rx);
        SessionStatusChanged?.Invoke(alive);

        if (!alive && ActiveTarget is not null)
            _log("No ECU response in current answer window.");
    }

    private bool IsRealEcuResponseToTester(byte[] tx, byte[] rx)
    {
        if (rx is null || rx.Length == 0)
            return false;

        // Bei 3-Byte-Headern ist tx[2] die Tester-Adresse, normalerweise 0xF1.
        // Echte ECU-Antworten sehen z. B. so aus:
        // 82 F1 10 50 86 ...
        // 83 F1 10 7F 23 11 ...
        // 84 F1 10 61 F1 ...
        // Echo sieht dagegen wie der TX-Frame aus und hat an Position 1 die ECU-Target-ID,
        // nicht die Tester-Adresse. Deshalb reicht diese Prüfung zuverlässig gegen Echo-only.
        byte tester = 0xF1;

        if (tx is not null && tx.Length >= 3)
            tester = tx[2];
        else if (_profile is not null)
            tester = HexUtil.ParseByte(_profile.TesterAddress);

        for (int i = 0; i <= rx.Length - 3; i++)
        {
            if (rx[i] >= 0x80 && rx[i + 1] == tester)
                return true;
        }

        return false;
    }

    public static byte[] BuildKwpFrame(byte target, byte source, params byte[] data)
    {
        if (data.Length > 0x3F)
            throw new ArgumentOutOfRangeException(nameof(data), "Dieses einfache 0x80-Length-Format ist nur bis 0x3F Datenbytes gedacht.");

        var frame = new List<byte>(data.Length + 4)
        {
            (byte)(0x80 + data.Length),
            target,
            source
        };

        frame.AddRange(data);

        byte checksum = KwpChecksum.Calculate(frame.ToArray());
        frame.Add(checksum);

        return frame.ToArray();
    }

    public static bool IsPositive2CResponse(byte[] rx, byte localIdentifier)
    {
        if (rx is null || rx.Length < 2)
            return false;

        for (int i = 0; i < rx.Length - 1; i++)
        {
            if (rx[i] == 0x6C && rx[i + 1] == localIdentifier)
                return true;
        }

        return false;
    }

    public bool WriteMemory(int address24, byte[] data, string? writeMethod = null, string? readMethodForVerify = null, bool verify = true)
    {
        EnsureReady();

        string method = NormalizeWriteMethod(string.IsNullOrWhiteSpace(writeMethod) ? _profile!.RamWriteMethod : writeMethod);

        if (method == "3D")
            return WriteMemoryByAddress3D(address24, data, readMethodForVerify, verify);

        if (method == "DDLI3B")
            return WriteMemoryByDdli3B(address24, data);

        throw new NotSupportedException("Unbekannte RAM-Write-Methode: " + method + ". Unterstützt: 3D, DDLI3B.");
    }

    public bool ForceWriteMemoryFast(int address24, byte[] data, string? writeMethod = null)
    {
        EnsureReady();

        string method = NormalizeWriteMethod(string.IsNullOrWhiteSpace(writeMethod) ? _profile!.RamWriteMethod : writeMethod);

        if (method == "3D")
            return WriteMemoryByAddress3DFast(address24, data);

        if (method == "DDLI3B")
            return ForceWriteMemoryByDdli3BFast(address24, data);

        throw new NotSupportedException("Unbekannte RAM-Write-Methode: " + method + ". Unterstützt: 3D, DDLI3B.");
    }

    /// <summary>
    /// RAM schreiben über KWP2000 $3D WriteMemoryByAddress.
    /// Das ist der bestätigte Schreibweg für ME7.5/C167, während DDLI/3B auf manchen ME7.5 mit 7F 2C 12 abgelehnt wird.
    /// </summary>
    public bool WriteMemoryByAddress3D(int address24, byte[] data, string? readMethodForVerify = null, bool verify = true)
    {
        EnsureReady();

        if (address24 < 0 || address24 > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(address24), "RAM-Adresse muss im 24-bit Bereich 0x000000-0xFFFFFF liegen.");

        if (data is null || data.Length == 0)
            throw new ArgumentException("Es müssen mindestens 1 Byte Daten geschrieben werden.", nameof(data));

        if (data.Length > 0x3A)
            throw new ArgumentOutOfRangeException(nameof(data), "$3D in diesem Tool bitte nur für kleine RAM-Blöcke verwenden.");

        byte target = ActiveTarget!.Value;
        byte source = HexUtil.ParseByte(_profile!.TesterAddress);
        int timeoutMs = _profile.Timing.ReadMemoryTimeoutMs > 0
            ? _profile.Timing.ReadMemoryTimeoutMs
            : _profile.Timing.DefaultResponseTimeoutMs;

        byte[] tx = BuildWriteMemoryByAddress3DFrame(target, source, address24, data);

        KwpCommandResult writeResult;
        lock (_ioLock)
        {
            _busy = true;

            try
            {
                _log($"3D write start: addr 0x{address24:X6}, len {data.Length}, data {HexUtil.Format(data)}");
                writeResult = SendRawInsideLock(tx, timeoutMs, log: false);

                if (!writeResult.HasResponse)
                    throw new TimeoutException($"3D write timeout at 0x{address24:X6}");

                if (!IsPositive3DResponse(writeResult.Rx))
                    throw new InvalidOperationException("3D write failed. RX: " + HexUtil.Format(writeResult.Rx));
            }
            finally
            {
                _busy = false;
            }
        }

        if (!verify)
        {
            _log($"3D write accepted: 0x{address24:X6} = {HexUtil.Format(data)}");
            return true;
        }

        byte[] verifyData = ReadMemory(address24, data.Length, readMethodForVerify);
        bool verified = verifyData.Take(data.Length).SequenceEqual(data);
        _log(verified
            ? $"3D write verified: 0x{address24:X6} = {HexUtil.Format(data)}"
            : $"3D write verify mismatch at 0x{address24:X6}. Wanted {HexUtil.Format(data)}, got {HexUtil.Format(verifyData)}");

        return verified;
    }

    /// <summary>
    /// Schneller Force-Write-Pfad für $3D: ein einzelner WriteMemoryByAddress Request, kein Verify.
    /// Für ForceWrite 0 ms ist das der schnellste bestätigte ME7.5-Schreibmodus.
    /// </summary>
    public bool WriteMemoryByAddress3DFast(int address24, byte[] data)
    {
        return WriteMemoryByAddress3D(address24, data, readMethodForVerify: null, verify: false);
    }

    private byte[] BuildWriteMemoryByAddress3DFrame(byte target, byte source, int address24, byte[] data)
    {
        if (data.Length <= 0 || data.Length > 0x3A)
            throw new ArgumentOutOfRangeException(nameof(data), "$3D Datenlänge ist ungültig.");

        byte addrHigh = (byte)((address24 >> 16) & 0xFF);
        byte addrMid = (byte)((address24 >> 8) & 0xFF);
        byte addrLow = (byte)(address24 & 0xFF);

        byte[] payload = new byte[5 + data.Length];
        payload[0] = 0x3D;
        payload[1] = addrHigh;
        payload[2] = addrMid;
        payload[3] = addrLow;
        payload[4] = (byte)data.Length;
        Array.Copy(data, 0, payload, 5, data.Length);

        return BuildKwpFrame(target, source, payload);
    }

    private bool IsPositive3DResponse(byte[] rx)
    {
        if (rx is null || rx.Length == 0)
            return false;

        if (_profile is not null && _profile.Commands.PositiveWriteMemoryContains.Count > 0)
            return HexUtil.ContainsAnySequence(rx, _profile.Commands.PositiveWriteMemoryContains);

        return Array.IndexOf(rx, (byte)0x7D) >= 0;
    }

    /// <summary>
    /// RAM schreiben über Bosch/VAG DDLI: $2C LocalIdentifier auf Adresse/Länge definieren,
    /// anschließend per $3B schreiben und per $21 verifizieren.
    /// Direkte $3D-WriteMemory-Logik wird bewusst nicht benutzt.
    /// </summary>
    public bool WriteMemoryByDdli3B(int address24, byte[] data, byte? localIdentifierOverride = null)
    {
        EnsureReady();

        if (address24 < 0 || address24 > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(address24), "RAM-Adresse muss im 24-bit Bereich 0x000000-0xFFFFFF liegen.");

        if (data is null || data.Length == 0)
            throw new ArgumentException("Es müssen mindestens 1 Byte Daten geschrieben werden.", nameof(data));

        if (data.Length > 0x3D)
            throw new ArgumentOutOfRangeException(nameof(data), "DDLI/$3B in diesem Tool bitte nur für kleine Blöcke verwenden.");

        // Der sichere Write-Pfad definiert den LocalIdentifier bewusst neu und verifiziert per 21.
        // Dadurch ist eine evtl. vorhandene schnelle Force-Write-Definition nicht mehr zuverlässig.
        InvalidateDdliFastWriteDefinition();

        byte target = ActiveTarget!.Value;
        byte source = HexUtil.ParseByte(_profile!.TesterAddress);
        byte localIdentifier = localIdentifierOverride ?? HexUtil.ParseByte(_profile.Ddli.LocalIdentifier);
        int timeoutMs = _profile.Timing.ReadMemoryTimeoutMs > 0
            ? _profile.Timing.ReadMemoryTimeoutMs
            : _profile.Timing.DefaultResponseTimeoutMs;
        int pauseMs = Math.Max(0, _profile.Ddli.CommandPauseMs);

        lock (_ioLock)
        {
            _busy = true;

            try
            {
                _log($"DDLI3B write start: addr 0x{address24:X6}, len {data.Length}, LI 0x{localIdentifier:X2}, data {HexUtil.Format(data)}");

                // 1. LocalIdentifier löschen, damit keine alte Definition hängen bleibt.
                byte[] clearFrame = BuildKwpFrame(target, source, 0x2C, localIdentifier, 0x04);
                KwpCommandResult clearResult = SendRawInsideLock(clearFrame, timeoutMs, log: false);

                if (!clearResult.HasResponse)
                    throw new TimeoutException($"DDLI write: 2C clear timeout at 0x{address24:X6}");

                if (!IsPositive2CResponse(clearResult.Rx, localIdentifier))
                    throw new InvalidOperationException("DDLI write: 2C clear failed. RX: " + HexUtil.Format(clearResult.Rx));

                if (pauseMs > 0)
                    Thread.Sleep(pauseMs);

                // 2. LocalIdentifier auf RAM-Adresse + Länge definieren.
                byte addrHigh = (byte)((address24 >> 16) & 0xFF);
                byte addrMid = (byte)((address24 >> 8) & 0xFF);
                byte addrLow = (byte)(address24 & 0xFF);

                byte[] defineFrame = BuildKwpFrame(
                    target,
                    source,
                    0x2C,
                    localIdentifier,
                    0x03,
                    0x01,
                    (byte)data.Length,
                    addrHigh,
                    addrMid,
                    addrLow);

                KwpCommandResult defineResult = SendRawInsideLock(defineFrame, timeoutMs, log: false);

                if (!defineResult.HasResponse)
                    throw new TimeoutException($"DDLI write: 2C define timeout at 0x{address24:X6}");

                if (!IsPositive2CResponse(defineResult.Rx, localIdentifier))
                    throw new InvalidOperationException("DDLI write: 2C define failed. RX: " + HexUtil.Format(defineResult.Rx));

                if (pauseMs > 0)
                    Thread.Sleep(pauseMs);

                // 3. Per $3B auf den definierten LocalIdentifier schreiben.
                byte[] writeData = new byte[data.Length + 2];
                writeData[0] = 0x3B;
                writeData[1] = localIdentifier;
                Array.Copy(data, 0, writeData, 2, data.Length);

                byte[] writeFrame = BuildKwpFrame(target, source, writeData);
                KwpCommandResult writeResult = SendRawInsideLock(writeFrame, timeoutMs, log: false);

                if (!writeResult.HasResponse)
                    throw new TimeoutException($"DDLI write: 3B timeout at 0x{address24:X6}");

                if (!IsPositive3BResponse(writeResult.Rx, localIdentifier))
                    throw new InvalidOperationException("DDLI write: 3B failed. RX: " + HexUtil.Format(writeResult.Rx));

                if (pauseMs > 0)
                    Thread.Sleep(pauseMs);

                // 4. Mit $21 zurücklesen und verifizieren.
                byte[] readFrame = BuildKwpFrame(target, source, 0x21, localIdentifier);
                KwpCommandResult readResult = SendRawInsideLock(readFrame, timeoutMs, log: false);

                if (!readResult.HasResponse)
                    throw new TimeoutException($"DDLI write: 21 verify timeout at 0x{address24:X6}");

                if (!TryExtract21Data(readResult.Rx, localIdentifier, data.Length, out byte[] verifyData))
                    throw new InvalidOperationException("DDLI write: 21 verify extract failed. RX: " + HexUtil.Format(readResult.Rx));

                bool verified = verifyData.SequenceEqual(data);
                _log(verified
                    ? $"DDLI3B write verified: 0x{address24:X6} = {HexUtil.Format(data)}"
                    : $"DDLI3B write verify mismatch at 0x{address24:X6}. Wanted {HexUtil.Format(data)}, got {HexUtil.Format(verifyData)}");

                return verified;
            }
            finally
            {
                _busy = false;
            }
        }
    }

    /// <summary>
    /// Schneller Force-Write-Pfad für Interval 0 ms:
    /// Der DDLI LocalIdentifier wird nur neu definiert, wenn Adresse/Länge/LI nicht bereits passen.
    /// Danach wird pro Tick nur noch $3B [LI] [Daten...] gesendet.
    /// Keine $21-Verifikation, weil hier das schnelle Halten eines RAM-Wertes Vorrang hat.
    /// </summary>
    public bool ForceWriteMemoryByDdli3BFast(int address24, byte[] data, byte? localIdentifierOverride = null)
    {
        EnsureReady();

        if (address24 < 0 || address24 > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(address24), "RAM-Adresse muss im 24-bit Bereich 0x000000-0xFFFFFF liegen.");

        if (data is null || data.Length == 0)
            throw new ArgumentException("Es müssen mindestens 1 Byte Daten geschrieben werden.", nameof(data));

        if (data.Length > 0x3D)
            throw new ArgumentOutOfRangeException(nameof(data), "DDLI/$3B in diesem Tool bitte nur für kleine Blöcke verwenden.");

        byte target = ActiveTarget!.Value;
        byte source = HexUtil.ParseByte(_profile!.TesterAddress);
        byte localIdentifier = localIdentifierOverride ?? HexUtil.ParseByte(_profile.Ddli.LocalIdentifier);
        int timeoutMs = _profile.Timing.DefaultResponseTimeoutMs > 0
            ? _profile.Timing.DefaultResponseTimeoutMs
            : 3000;
        int pauseMs = Math.Max(0, _profile.Ddli.CommandPauseMs);

        lock (_ioLock)
        {
            _busy = true;

            try
            {
                bool definitionMatches =
                    _ddliFastWriteAddress == address24 &&
                    _ddliFastWriteLength == data.Length &&
                    _ddliFastWriteLocalIdentifier == localIdentifier;

                if (!definitionMatches)
                {
                    DefineDdliInsideLock(target, source, localIdentifier, address24, data.Length, timeoutMs, pauseMs);
                    _ddliFastWriteAddress = address24;
                    _ddliFastWriteLength = data.Length;
                    _ddliFastWriteLocalIdentifier = localIdentifier;
                    _log($"DDLI fast force-write defined: LI 0x{localIdentifier:X2} -> 0x{address24:X6}, len {data.Length}");
                }

                byte[] writeData = new byte[data.Length + 2];
                writeData[0] = 0x3B;
                writeData[1] = localIdentifier;
                Array.Copy(data, 0, writeData, 2, data.Length);

                byte[] writeFrame = BuildKwpFrame(target, source, writeData);
                KwpCommandResult writeResult = SendRawInsideLock(writeFrame, timeoutMs, log: false);

                if (!writeResult.HasResponse)
                    throw new TimeoutException($"DDLI fast force-write: 3B timeout at 0x{address24:X6}");

                if (!IsPositive3BResponse(writeResult.Rx, localIdentifier))
                    throw new InvalidOperationException("DDLI fast force-write: 3B failed. RX: " + HexUtil.Format(writeResult.Rx));

                return true;
            }
            catch
            {
                InvalidateDdliFastWriteDefinition();
                throw;
            }
            finally
            {
                _busy = false;
            }
        }
    }

    private void DefineDdliInsideLock(byte target, byte source, byte localIdentifier, int address24, int length, int timeoutMs, int pauseMs)
    {
        byte[] clearFrame = BuildKwpFrame(target, source, 0x2C, localIdentifier, 0x04);
        KwpCommandResult clearResult = SendRawInsideLock(clearFrame, timeoutMs, log: false);

        if (!clearResult.HasResponse)
            throw new TimeoutException($"DDLI define: 2C clear timeout at 0x{address24:X6}");

        if (!IsPositive2CResponse(clearResult.Rx, localIdentifier))
            throw new InvalidOperationException("DDLI define: 2C clear failed. RX: " + HexUtil.Format(clearResult.Rx));

        if (pauseMs > 0)
            Thread.Sleep(pauseMs);

        byte addrHigh = (byte)((address24 >> 16) & 0xFF);
        byte addrMid = (byte)((address24 >> 8) & 0xFF);
        byte addrLow = (byte)(address24 & 0xFF);

        byte[] defineFrame = BuildKwpFrame(
            target,
            source,
            0x2C,
            localIdentifier,
            0x03,
            0x01,
            (byte)length,
            addrHigh,
            addrMid,
            addrLow);

        KwpCommandResult defineResult = SendRawInsideLock(defineFrame, timeoutMs, log: false);

        if (!defineResult.HasResponse)
            throw new TimeoutException($"DDLI define: 2C define timeout at 0x{address24:X6}");

        if (!IsPositive2CResponse(defineResult.Rx, localIdentifier))
            throw new InvalidOperationException("DDLI define: 2C define failed. RX: " + HexUtil.Format(defineResult.Rx));

        if (pauseMs > 0)
            Thread.Sleep(pauseMs);
    }

    private void InvalidateDdliFastWriteDefinition()
    {
        _ddliFastWriteAddress = null;
        _ddliFastWriteLength = 0;
        _ddliFastWriteLocalIdentifier = null;
    }

    public static bool IsPositive3BResponse(byte[] rx, byte localIdentifier)
    {
        if (rx is null || rx.Length < 2)
            return false;

        for (int i = 0; i < rx.Length - 1; i++)
        {
            if (rx[i] == 0x7B && rx[i + 1] == localIdentifier)
                return true;
        }

        return false;
    }

    public static bool TryExtract21Data(
        byte[] rx,
        byte localIdentifier,
        int expectedLength,
        out byte[] data)
    {
        data = Array.Empty<byte>();

        if (rx is null || expectedLength < 0 || rx.Length < 2 + expectedLength)
            return false;

        for (int i = 0; i <= rx.Length - 2; i++)
        {
            if (rx[i] == 0x61 && rx[i + 1] == localIdentifier)
            {
                int dataStart = i + 2;
                int available = rx.Length - dataStart;

                if (available < expectedLength)
                    return false;

                data = rx.Skip(dataStart).Take(expectedLength).ToArray();
                return true;
            }
        }

        return false;
    }

    private static void AppendFailedBlock(List<byte> result, int length, bool fillFailedBlocks, byte failedFillByte)
    {
        if (!fillFailedBlocks)
            return;

        for (int i = 0; i < length; i++)
            result.Add(failedFillByte);
    }

    private byte[] ExtractReadMemoryPayload(byte[] rx, int expectedLength)
    {
        int serviceIndex = Array.IndexOf(rx, (byte)0x63);
        if (serviceIndex < 0)
            throw new InvalidOperationException("ReadMemory Antwort enthält kein 0x63: " + HexUtil.Format(rx));

        int available = rx.Length - serviceIndex - 1;
        if (available <= 0)
            return Array.Empty<byte>();

        int take = Math.Min(expectedLength, available);
        return rx.Skip(serviceIndex + 1).Take(take).ToArray();
    }

    private static bool IsPositive(byte[] rx, IEnumerable<string> positiveSequences)
    {
        return rx.Length > 0 && HexUtil.ContainsAnySequence(rx, positiveSequences);
    }

    private static string NormalizeReadMethod(string? readMethod)
    {
        string method = (readMethod ?? "23").Trim().ToUpperInvariant();
        method = method.Replace("$", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase);

        return method switch
        {
            "23" or "READMEMORYBYADDRESS" or "SERVICE23" => "23",
            "21" or "DDLI" or "DDLI21" or "2C21" or "2CDDLI21" or "SERVICE21" => "DDLI21",
            _ => method
        };
    }


    private static string NormalizeWriteMethod(string? writeMethod)
    {
        string method = (writeMethod ?? "DDLI3B").Trim().ToUpperInvariant();
        method = method.Replace("$", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase);

        return method switch
        {
            "3D" or "WRITEMEMORYBYADDRESS" or "SERVICE3D" => "3D",
            "3B" or "DDLI" or "DDLI3B" or "2C3B" or "2CDDLI3B" or "SERVICE3B" => "DDLI3B",
            _ => method
        };
    }

    private void StartKeepAlive()
    {
        StopKeepAlive();
        _keepAliveCts = new CancellationTokenSource();
        CancellationToken token = _keepAliveCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_profile!.Timing.KeepAliveIntervalMs, token);
                    if (!token.IsCancellationRequested && !_busy)
                        CheckSessionAliveQuiet();
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _log("KeepAlive error: " + ex.Message);
                }
            }
        }, token);
    }

    private void StopKeepAlive()
    {
        try { _keepAliveCts?.Cancel(); } catch { }
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
    }

    public void Disconnect()
    {
        StopKeepAlive();
        ActiveTarget = null;
        InvalidateDdliFastWriteDefinition();
        _transport.Close();
        SessionStatusChanged?.Invoke(false);
    }

    private void EnsureReady()
    {
        EnsureProfile();
        if (ActiveTarget is null)
            throw new InvalidOperationException("Keine aktive Target-ID.");
    }

    private void EnsureProfile()
    {
        if (_profile is null || _builder is null)
            throw new InvalidOperationException("Kein Profil geladen.");
    }

    public void Dispose() => Disconnect();
}

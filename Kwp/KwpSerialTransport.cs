using System.Diagnostics;
using System.IO.Ports;
using EcuRamTool.Profiles;

namespace EcuRamTool.Kwp;

public sealed class KwpSerialTransport : IDisposable
{
    private readonly Action<string> _log;
    private SerialPort? _port;

    public bool IsOpen => _port is { IsOpen: true };

    public KwpSerialTransport(Action<string> log)
    {
        _log = log;
    }

    public void Open(string portName, EcuProfile profile)
    {
        Close();
        _port = new SerialPort(portName)
        {
            BaudRate = profile.BaudRate,
            DataBits = profile.DataBits,
            Parity = Enum.Parse<Parity>(profile.Parity, ignoreCase: true),
            StopBits = Enum.Parse<StopBits>(profile.StopBits, ignoreCase: true),
            Handshake = Handshake.None,
            ReadTimeout = profile.Timing.DefaultResponseTimeoutMs,
            WriteTimeout = profile.Timing.SerialWriteTimeoutMs,
            DtrEnable = true,
            RtsEnable = true
        };
        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
    }

    public void FastInit(EcuProfile profile)
    {
        EnsureOpen();
        Thread.Sleep(profile.Init.PreOpenDelayMs);

        SetKLineHigh();
        Thread.Sleep(profile.Init.FastInitHighBeforeMs);

        SetKLineLow();
        PreciseDelayMs(profile.Init.FastInitLowMs);

        SetKLineHigh();
        PreciseDelayMs(profile.Init.FastInitHighAfterMs);

        _port!.DiscardInBuffer();
    }

    public void Tx(byte[] tx)
    {
        EnsureOpen();
        _port!.DiscardInBuffer();
        _port.Write(tx, 0, tx.Length);
    }

    public byte[] RxUntilIdleOrTimeout(int timeoutMs, int idleMs, int pollSleepMs)
    {
        EnsureOpen();
        var result = new List<byte>();
        long start = Environment.TickCount64;
        long lastByteAt = -1;

        while (Environment.TickCount64 - start < timeoutMs)
        {
            bool gotByte = false;
            while (_port!.BytesToRead > 0)
            {
                int b = _port.ReadByte();
                if (b >= 0)
                {
                    result.Add((byte)b);
                    lastByteAt = Environment.TickCount64;
                    gotByte = true;
                }
            }

            if (result.Count > 0 && lastByteAt >= 0 && Environment.TickCount64 - lastByteAt >= idleMs)
                break;

            if (!gotByte && pollSleepMs > 0)
                Thread.Sleep(pollSleepMs);
        }

        return result.ToArray();
    }

    public byte[] ReadExact(int count, int timeoutMs, int pollSleepMs)
    {
        EnsureOpen();
        var result = new List<byte>(count);
        long start = Environment.TickCount64;

        while (result.Count < count && Environment.TickCount64 - start < timeoutMs)
        {
            while (_port!.BytesToRead > 0 && result.Count < count)
            {
                int b = _port.ReadByte();
                if (b >= 0)
                    result.Add((byte)b);
            }
            if (pollSleepMs > 0)
                Thread.Sleep(pollSleepMs);
        }

        return result.ToArray();
    }

    public void Close()
    {
        try
        {
            if (_port is { IsOpen: true })
                _port.Close();
        }
        catch { }
        _port?.Dispose();
        _port = null;
    }

    private void SetKLineLow()
    {
        EnsureOpen();
        _port!.BreakState = true;
    }

    private void SetKLineHigh()
    {
        EnsureOpen();
        _port!.BreakState = false;
    }

    private static void PreciseDelayMs(double ms)
    {
        long targetTicks = (long)(Stopwatch.Frequency * ms / 1000.0);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedTicks < targetTicks)
            Thread.SpinWait(50);
    }

    private void EnsureOpen()
    {
        if (_port is null || !_port.IsOpen)
            throw new InvalidOperationException("Serial port not open.");
    }

    public void Dispose() => Close();
}

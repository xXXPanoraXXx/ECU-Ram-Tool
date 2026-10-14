using System.ComponentModel;
using System.Globalization;
using EcuRamTool.Kwp;
using EcuRamTool.Profiles;
using EcuRamTool.Ram;

namespace EcuRamTool.Gui;

public sealed class WatchPanel : UserControl
{
    private readonly KwpSession _session;
    private readonly Action<string> _log;
    private readonly BindingList<WatchItem> _items = new();
    private readonly DataGridView _grid = new();
    private readonly Button _manualAddButton = new();
    private readonly CheckBox _stopReadingCheckBox = new();
    private readonly CheckBox _stopForceWriteCheckBox = new();
    private readonly ContextMenuStrip _contextMenu = new();
    private readonly ToolStripMenuItem _optionsMenuItem = new("Optionen...");
    private readonly ToolStripMenuItem _writeMenuItem = new("Write this address...");
    private readonly ToolStripMenuItem _addToEcuMenuItem = new("Add to .ecu");
    private readonly ToolStripMenuItem _removeMenuItem = new("Remove");
    private readonly System.Windows.Forms.Timer _autoRefreshTimer = new();
    private readonly System.Windows.Forms.Timer _forceWriteTimer = new();
    private readonly Dictionary<WatchItem, DateTime> _nextForceWriteDueUtc = new();
    private bool _autoRefreshRunning;
    private bool _forceWriteRunning;
    private bool _manualWritePending;
    private bool _manualWriteRunning;
    private Func<bool>? _externalBusyProvider;
    private EcuProfile? _profile;
    private string? _ecuFilePath;

    public string? EcuFilePath
    {
        get => _ecuFilePath;
        set => _ecuFilePath = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public bool StopReading
    {
        get => _stopReadingCheckBox.Checked;
        set => _stopReadingCheckBox.Checked = value;
    }

    public bool StopForceWrite
    {
        get => _stopForceWriteCheckBox.Checked;
        set => _stopForceWriteCheckBox.Checked = value;
    }

    public WatchPanel(KwpSession session, Action<string> log)
    {
        _session = session;
        _log = log;
        Dock = DockStyle.Fill;
        Padding = new Padding(8);

        _manualAddButton.Text = "Add Manual";
        _manualAddButton.SetBounds(8, 8, 105, 30);
        _manualAddButton.Click += (_, _) => AddManual();
        Controls.Add(_manualAddButton);

        _stopReadingCheckBox.Text = "Stop reading";
        _stopReadingCheckBox.AutoSize = true;
        _stopReadingCheckBox.SetBounds(125, 14, 110, 22);
        Controls.Add(_stopReadingCheckBox);

        _stopForceWriteCheckBox.Text = "Stop ForceWrite";
        _stopForceWriteCheckBox.AutoSize = true;
        _stopForceWriteCheckBox.SetBounds(245, 14, 130, 22);
        Controls.Add(_stopForceWriteCheckBox);

        BuildContextMenu();
        BuildGrid();
        DarkTheme.ApplyChildren(this);
        StyleContextMenu();

        _autoRefreshTimer.Interval = 750;
        _autoRefreshTimer.Tick += async (_, _) => await AutoRefreshTickAsync();
        _autoRefreshTimer.Start();

        // Läuft nur, wenn Force-Write-Items existieren und kein RAM-Scan aktiv ist.
        // 25 ms ist kein Schreibintervall, sondern nur die Scheduler-Prüfung.
        // Bei ForceWrite-Intervall 0 wird so oft geschrieben, wie Bus/Scheduler es zulassen.
        _forceWriteTimer.Interval = 25;
        _forceWriteTimer.Tick += async (_, _) => await ForceWriteTickAsync();
        _forceWriteTimer.Start();

        Resize += (_, _) => LayoutControlsSafe();
        Load += (_, _) => LayoutControlsSafe();
    }

    public void SetExternalBusyProvider(Func<bool> busyProvider)
    {
        _externalBusyProvider = busyProvider;
    }

    private bool IsExternalBusy => _externalBusyProvider?.Invoke() == true;

    private void BuildContextMenu()
    {
        _optionsMenuItem.Click += (_, _) => EditSelected();
        _writeMenuItem.Click += async (_, _) => await WriteSelectedAddressAsync();
        _addToEcuMenuItem.Click += (_, _) => AddSelectedToEcu();
        _removeMenuItem.Click += (_, _) => RemoveSelected();
        _contextMenu.Opening += (_, e) =>
        {
            int selectedCount = SelectedItems.Count;
            if (selectedCount == 0)
            {
                e.Cancel = true;
                return;
            }

            bool single = selectedCount == 1;
            _optionsMenuItem.Visible = single;
            _writeMenuItem.Visible = single;
            _addToEcuMenuItem.Visible = single;
            _removeMenuItem.Visible = true;

            _optionsMenuItem.Enabled = single;
            _writeMenuItem.Enabled = single;
            _addToEcuMenuItem.Enabled = single;
            _removeMenuItem.Enabled = true;
            _removeMenuItem.Text = single ? "Remove" : $"Remove selected ({selectedCount})";
        };
        _contextMenu.Items.Add(_optionsMenuItem);
        _contextMenu.Items.Add(_writeMenuItem);
        _contextMenu.Items.Add(_addToEcuMenuItem);
        _contextMenu.Items.Add(_removeMenuItem);
    }

    private void BuildGrid()
    {
        _grid.SetBounds(8, 46, 300, 520);
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _items;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.RowHeadersVisible = false;
        _grid.ContextMenuStrip = _contextMenu;
        _grid.CellMouseDown += GridOnCellMouseDown;
        _grid.KeyDown += GridOnKeyDown;

        AddColumn("Name", "Name", 150, false);
        AddColumn("ValueText", "Value", 90, true);
        AddColumn("UnitText", "Unit", 70, true);
        AddColumn("TypeText", "Type", 130, true);
        AddColumn("AddressText", "Address", 135, true);
        AddColumn("RawValueText", "Raw Value", 95, true);
        Controls.Add(_grid);
    }

    private void GridOnCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;

        // Bei Rechtsklick auf eine bereits markierte Zeile die bestehende Mehrfachauswahl behalten.
        // Bei Rechtsklick auf eine nicht markierte Zeile wie Windows üblich auf diese einzelne Zeile wechseln.
        if (!_grid.Rows[e.RowIndex].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;
        }

        int columnIndex = e.ColumnIndex >= 0 ? e.ColumnIndex : 0;
        _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[columnIndex];
    }

    private void GridOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A)
        {
            _grid.SelectAll();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void LayoutControlsSafe()
    {
        int width = Math.Max(280, ClientSize.Width - 16);
        int height = Math.Max(240, ClientSize.Height - 16);

        _manualAddButton.SetBounds(8, 8, 105, 30);
        _stopReadingCheckBox.SetBounds(125, 14, 110, 22);
        _stopForceWriteCheckBox.SetBounds(245, 14, 130, 22);
        _grid.SetBounds(8, 46, width, Math.Max(160, height - 54));
    }

    public void SetProfile(EcuProfile profile)
    {
        _profile = profile;
        int interval = profile.Timing.WatchRefreshIntervalMs;
        _autoRefreshTimer.Interval = Math.Max(250, interval <= 0 ? 750 : interval);
    }

    public List<WatchItem> GetItemsSnapshot()
    {
        return _items.Select(CloneItem).ToList();
    }

    public void ReplaceItems(IEnumerable<WatchItem> items)
    {
        _items.Clear();
        _nextForceWriteDueUtc.Clear();
        foreach (WatchItem item in items)
            _items.Add(CloneItem(item));
        _grid.Refresh();
    }

    public void AddCandidate(ScanCandidate c)
    {
        if (_items.Any(x => x.Address24 == c.Address24 && x.ValueType == c.ValueType && x.Bit == c.Bit && x.MaskValue == 0))
            return;

        _items.Add(new WatchItem
        {
            Address24 = c.Address24,
            ValueType = c.ValueType,
            Bit = c.Bit,
            LastRawValue = c.RawValue,
            Factor = c.Factor,
            Name = c.Name,
            Unit = string.Empty,
            ReadMethod = c.ReadMethod,
            WriteMethod = c.WriteMethod
        });
    }

    private async Task AutoRefreshTickAsync()
    {
        if (_autoRefreshRunning)
            return;

        if (_items.Count == 0)
            return;

        if (StopReading)
            return;

        if (!_session.IsOpen || _session.ActiveTarget is null || _session.IsBusy || IsExternalBusy || _manualWritePending || _manualWriteRunning)
            return;

        await RefreshWatchAsync(silent: true);
    }

    private async Task RefreshWatchAsync(bool silent)
    {
        if (_autoRefreshRunning)
            return;

        if (!_session.IsOpen || _session.ActiveTarget is null)
        {
            if (!silent)
                MessageBox.Show("Nicht verbunden.");
            return;
        }

        if (StopReading)
            return;

        if (_session.IsBusy || IsExternalBusy || _manualWritePending || _manualWriteRunning)
            return;

        _autoRefreshRunning = true;

        try
        {
            foreach (WatchItem item in _items)
            {
                if (!_session.IsOpen || _session.ActiveTarget is null)
                    break;

                if (_manualWritePending || _manualWriteRunning || _session.IsBusy || IsExternalBusy)
                    break;

                string? readMethod = string.IsNullOrWhiteSpace(item.ReadMethod) ? null : item.ReadMethod;
                byte[] payload = await Task.Run(() => _session.ReadMemory(item.Address24, item.ByteLength, readMethod));
                if (payload.Length == 0)
                    continue;

                ApplyReadPayloadToItem(item, payload);
            }

            _grid.Refresh();
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(ex.Message);
            _log("WATCH ERROR: " + ex.Message);
        }
        finally
        {
            _autoRefreshRunning = false;
        }
    }

    private async Task ForceWriteTickAsync()
    {
        if (_forceWriteRunning)
            return;

        if (_items.Count == 0 || !_items.Any(x => x.ForceWriteEnabled))
            return;

        if (StopForceWrite)
            return;

        if (!_session.IsOpen || _session.ActiveTarget is null || _session.IsBusy || IsExternalBusy || _manualWritePending || _manualWriteRunning)
            return;

        _forceWriteRunning = true;

        try
        {
            DateTime now = DateTime.UtcNow;
            foreach (WatchItem item in _items.Where(x => x.ForceWriteEnabled).ToList())
            {
                if (!_session.IsOpen || _session.ActiveTarget is null || _session.IsBusy || IsExternalBusy || _manualWritePending || _manualWriteRunning)
                    break;

                if (_nextForceWriteDueUtc.TryGetValue(item, out DateTime due) && now < due)
                    continue;

                byte[] data = RawValueToBytes(item, item.ForceWriteRawValue);
                int intervalMs = Math.Max(0, item.ForceWriteIntervalMs);

                // ForceWrite ist ein Halte-/Ballermodus: Verify ist absichtlich AUS.
                // Bei intervalMs == 0 wird der schnellste Pfad genutzt.
                // Bei intervalMs > 0 wird nur mit Verzögerung geschrieben, aber ebenfalls ohne Verify,
                // weil viele ECU-RAM-Werte sofort wieder vom ECU-Task überschrieben werden.
                bool ok = intervalMs == 0
                    ? await Task.Run(() => _session.ForceWriteMemoryFast(item.Address24, data, item.WriteMethod))
                    : await Task.Run(() => _session.WriteMemory(item.Address24, data, item.WriteMethod, item.ReadMethod, verify: false));

                if (ok)
                    item.LastRawValue = item.ValueType == ScanValueType.BitMask
                        ? item.ForceWriteRawValue & Math.Max(0, item.Mask)
                        : item.ForceWriteRawValue;

                _nextForceWriteDueUtc[item] = intervalMs == 0 ? DateTime.UtcNow : DateTime.UtcNow.AddMilliseconds(intervalMs);
            }

            _grid.Refresh();
        }
        catch (Exception ex)
        {
            _log("FORCE WRITE ERROR: " + ex.Message);
        }
        finally
        {
            _forceWriteRunning = false;
        }
    }

    private void AddManual()
    {
        using var dialog = new ManualWatchItemDialog(_profile, null, _ecuFilePath);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (!string.IsNullOrWhiteSpace(dialog.SelectedEcuFilePath))
            _ecuFilePath = dialog.SelectedEcuFilePath;

        if (dialog.ImportedItems.Count > 0)
        {
            int added = 0;
            foreach (WatchItem imported in dialog.ImportedItems)
            {
                if (ContainsEquivalent(imported))
                    continue;

                _items.Add(imported);
                added++;
            }

            _log($".ecu watch items added: {added}/{dialog.ImportedItems.Count}" +
                 (string.IsNullOrWhiteSpace(_ecuFilePath) ? string.Empty : " from " + _ecuFilePath));
            return;
        }

        if (dialog.CreatedItem is null)
            return;

        WatchItem item = dialog.CreatedItem;
        if (ContainsEquivalent(item))
        {
            _log("Watch item already exists: " + item.AddressText);
            return;
        }

        _items.Add(item);
        _log("Manual watch item added: " + item.AddressText);
    }

    private void EditSelected()
    {
        WatchItem? selected = SelectedItem;
        if (selected is null)
            return;

        using var dialog = new ManualWatchItemDialog(_profile, selected, _ecuFilePath);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CreatedItem is null)
            return;

        bool forceWasActive = selected.ForceWriteEnabled;
        int forceRaw = selected.ForceWriteRawValue;
        int forceInterval = selected.ForceWriteIntervalMs;

        WatchItem edited = dialog.CreatedItem;
        edited.LastRawValue = selected.LastRawValue;

        CopyItem(edited, selected);
        selected.ForceWriteEnabled = forceWasActive;
        selected.ForceWriteRawValue = forceRaw;
        selected.ForceWriteIntervalMs = forceInterval;

        _grid.Refresh();
        _log("Watch item updated: " + selected.AddressText);
    }

    private async Task WriteSelectedAddressAsync()
    {
        WatchItem? selected = SelectedItem;
        if (selected is null)
            return;

        if (!_session.IsOpen || _session.ActiveTarget is null)
        {
            MessageBox.Show(this, "Nicht verbunden.", "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new WriteWatchItemDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (IsExternalBusy)
        {
            MessageBox.Show(this, "Schreiben ist nicht möglich, solange ein RAM-Scan aktiv ist.", "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _manualWritePending = true;

        try
        {
            await WaitForWatchIoIdleAsync();

            if (IsExternalBusy)
            {
                MessageBox.Show(this, "Schreiben ist nicht möglich, solange ein RAM-Scan aktiv ist.", "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _manualWriteRunning = true;
            _manualWritePending = false;

            WritePreparation prep = await Task.Run(() => PrepareWrite(selected, dialog.ValueText));

            bool ok;

            if (dialog.ForceWriteEnabled)
            {
                // ForceWrite ist absichtlich ohne Verify. Ziel ist, den Wert immer wieder in den RAM zu drücken,
                // auch wenn die ECU den Wert direkt wieder überschreibt.
                int intervalMs = Math.Max(0, dialog.IntervalMs);
                ok = intervalMs == 0
                    ? await Task.Run(() => _session.ForceWriteMemoryFast(selected.Address24, prep.Data, selected.WriteMethod))
                    : await Task.Run(() => _session.WriteMemory(selected.Address24, prep.Data, selected.WriteMethod, selected.ReadMethod, verify: false));

                if (!ok)
                {
                    MessageBox.Show(this, "ForceWrite konnte nicht gesendet werden.", "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _log("FORCE WRITE SEND FAILED: " + selected.AddressText);
                    return;
                }

                selected.ForceWriteEnabled = true;
                selected.ForceWriteRawValue = prep.FullRawValue;
                selected.ForceWriteIntervalMs = intervalMs;
                _nextForceWriteDueUtc[selected] = intervalMs == 0 ? DateTime.UtcNow : DateTime.UtcNow.AddMilliseconds(intervalMs);
                _log($"FORCE WRITE enabled: {selected.AddressText}, raw 0x{prep.FullRawValue:X}, interval {selected.ForceWriteIntervalMs} ms, verify off");
            }
            else
            {
                // Normaler Einzel-Write bleibt sicher: hier wird weiterhin verifiziert.
                ok = await Task.Run(() => _session.WriteMemory(selected.Address24, prep.Data, selected.WriteMethod, selected.ReadMethod, verify: true));
                if (!ok)
                {
                    MessageBox.Show(this, "Write wurde gesendet, aber Verify passt nicht.", "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _log("WRITE VERIFY MISMATCH: " + selected.AddressText);
                    return;
                }

                selected.ForceWriteEnabled = false;
                _nextForceWriteDueUtc.Remove(selected);
                _log($"WRITE OK: {selected.AddressText}, raw 0x{prep.FullRawValue:X}");
            }

            selected.LastRawValue = selected.ValueType == ScanValueType.BitMask
                ? prep.FullRawValue & Math.Max(0, selected.Mask)
                : prep.FullRawValue;

            _grid.Refresh();
        }
        catch (Exception ex)
        {
            _log("WRITE ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Write RAM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _manualWritePending = false;
            _manualWriteRunning = false;
        }
    }

    private async Task WaitForWatchIoIdleAsync()
    {
        // Kein harter Fehler, wenn gerade ein Watchlist-Read läuft:
        // Der manuelle Write wird kurz eingeplant und danach ausgeführt.
        for (int i = 0; i < 200; i++)
        {
            if (!_autoRefreshRunning && !_forceWriteRunning && !_session.IsBusy)
                return;

            await Task.Delay(10);
        }
    }

    private bool HasZeroLatencyForceWrite()
    {
        return _items.Any(x => x.ForceWriteEnabled && x.ForceWriteIntervalMs == 0);
    }

    private WritePreparation PrepareWrite(WatchItem item, string valueText)
    {
        int fullRawValue;

        if (item.ValueType == ScanValueType.BitMask)
        {
            int wanted = ParseWriteInt(valueText);
            int mask = item.Mask;
            if (mask <= 0)
                throw new InvalidOperationException("Statusbit/Flag braucht eine gültige Maske.");

            string? readMethod = string.IsNullOrWhiteSpace(item.ReadMethod) ? null : item.ReadMethod;
            byte[] currentPayload = _session.ReadMemory(item.Address24, item.ByteLength, readMethod);
            int currentRaw = BytesToRaw(currentPayload, item.ByteLength);
            fullRawValue = wanted != 0 ? currentRaw | mask : currentRaw & ~mask;
        }
        else
        {
            double scaledValue = ParseWriteDouble(valueText);
            double factor = Math.Abs(item.Factor) < 0.000000001 ? 1.0 : item.Factor;
            fullRawValue = (int)Math.Round(scaledValue / factor, MidpointRounding.AwayFromZero);
        }

        byte[] data = RawValueToBytes(item, fullRawValue);
        return new WritePreparation(fullRawValue, data);
    }

    private static void ApplyReadPayloadToItem(WatchItem item, byte[] payload)
    {
        if (item.ValueType == ScanValueType.BitMask)
        {
            int raw = payload[0];
            if (payload.Length >= 2 && item.ByteLength >= 2)
                raw = payload[0] | (payload[1] << 8);

            int mask = item.Mask;
            item.LastRawValue = mask > 0 ? raw & mask : raw;
        }
        else if (item.ValueType == ScanValueType.UInt8)
        {
            item.LastRawValue = payload[0];
        }
        else if (item.ValueType == ScanValueType.UInt16 && payload.Length >= 2)
        {
            item.LastRawValue = payload[0] | (payload[1] << 8);
        }
    }

    private static int BytesToRaw(byte[] payload, int byteLength)
    {
        if (payload.Length == 0)
            return 0;

        int raw = payload[0];
        if (byteLength >= 2 && payload.Length >= 2)
            raw |= payload[1] << 8;
        return raw;
    }

    private static byte[] RawValueToBytes(WatchItem item, int rawValue)
    {
        if (item.ValueType == ScanValueType.UInt8)
        {
            if (rawValue < 0 || rawValue > 0xFF)
                throw new ArgumentOutOfRangeException(nameof(rawValue), "8-bit Raw-Wert muss 0..255 sein.");
            return new[] { (byte)rawValue };
        }

        if (item.ValueType == ScanValueType.UInt16 || item.ByteLength >= 2)
        {
            if (rawValue < 0 || rawValue > 0xFFFF)
                throw new ArgumentOutOfRangeException(nameof(rawValue), "16-bit Raw-Wert muss 0..65535 sein.");
            return new[] { (byte)(rawValue & 0xFF), (byte)((rawValue >> 8) & 0xFF) };
        }

        if (rawValue < 0 || rawValue > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(rawValue), "8-bit Raw-Wert muss 0..255 sein.");
        return new[] { (byte)rawValue };
    }

    private static double ParseWriteDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Bitte einen Schreibwert eingeben.");

        string normalized = text.Trim().Replace(',', '.');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return HexUtil.ParseInt(normalized);

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException("Schreibwert ist keine gültige Zahl.");

        return value;
    }

    private static int ParseWriteInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Bitte 0 oder 1 eingeben.");

        string normalized = text.Trim().Replace(',', '.');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return HexUtil.ParseInt(normalized);

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException("Schreibwert ist keine gültige Zahl.");

        return Math.Abs(value) > 0.000000001 ? 1 : 0;
    }

    private void AddSelectedToEcu()
    {
        WatchItem? selected = SelectedItem;
        if (selected is null)
            return;

        string? path = EnsureEcuFilePath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            string addedName = EcuMeasurementWriter.AppendWatchItem(path, selected);
            _ecuFilePath = path;
            _log($"Added to .ecu: {addedName} -> {path}");
            MessageBox.Show(this, $"Adresse wurde zur .ecu hinzugefügt:\n{addedName}", "Add to .ecu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log("ADD TO ECU ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Add to .ecu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string? EnsureEcuFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_ecuFilePath) && File.Exists(_ecuFilePath))
            return _ecuFilePath;

        using var dialog = new OpenFileDialog
        {
            Title = ".ecu auswählen",
            Filter = "ME7Logger ECU Datei (*.ecu)|*.ecu|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return null;

        _ecuFilePath = dialog.FileName;
        return _ecuFilePath;
    }

    private void RemoveSelected()
    {
        List<WatchItem> selected = SelectedItems;
        if (selected.Count == 0)
            return;

        foreach (WatchItem item in selected)
        {
            item.ForceWriteEnabled = false;
            _nextForceWriteDueUtc.Remove(item);
            _items.Remove(item);
        }

        _log(selected.Count == 1
            ? "Watch item removed: " + selected[0].AddressText
            : $"Watch items removed: {selected.Count}");
    }

    private WatchItem? SelectedItem => _grid.CurrentRow?.DataBoundItem as WatchItem;

    private List<WatchItem> SelectedItems
    {
        get
        {
            return _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as WatchItem)
                .Where(item => item is not null)
                .Cast<WatchItem>()
                .Distinct()
                .ToList();
        }
    }

    private bool ContainsEquivalent(WatchItem item)
    {
        return _items.Any(x =>
            x.Address24 == item.Address24 &&
            x.ValueType == item.ValueType &&
            x.Bit == item.Bit &&
            x.Mask == item.Mask &&
            string.Equals(x.ReadMethod, item.ReadMethod, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.WriteMethod, item.WriteMethod, StringComparison.OrdinalIgnoreCase));
    }

    private static WatchItem CloneItem(WatchItem item)
    {
        return new WatchItem
        {
            Address24 = item.Address24,
            ValueType = item.ValueType,
            Bit = item.Bit,
            MaskValue = item.MaskValue,
            Name = item.Name,
            Unit = item.Unit ?? string.Empty,
            LastRawValue = item.LastRawValue,
            Factor = item.Factor,
            ReadMethod = item.ReadMethod,
            WriteMethod = item.WriteMethod,
            ForceWriteEnabled = false
        };
    }

    private static void CopyItem(WatchItem source, WatchItem target)
    {
        target.Address24 = source.Address24;
        target.ValueType = source.ValueType;
        target.Bit = source.Bit;
        target.MaskValue = source.MaskValue;
        target.Name = source.Name;
        target.Unit = source.Unit;
        target.LastRawValue = source.LastRawValue;
        target.Factor = source.Factor;
        target.ReadMethod = source.ReadMethod;
        target.WriteMethod = source.WriteMethod;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoRefreshTimer.Dispose();
            _forceWriteTimer.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AddColumn(string property, string header, int width, bool readOnly)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly
        });
    }

    private void StyleContextMenu()
    {
        _contextMenu.BackColor = DarkTheme.PanelColor;
        _contextMenu.ForeColor = DarkTheme.Fore;
        foreach (ToolStripItem item in _contextMenu.Items)
        {
            item.BackColor = DarkTheme.PanelColor;
            item.ForeColor = DarkTheme.Fore;
        }
    }

    private sealed record WritePreparation(int FullRawValue, byte[] Data);
}

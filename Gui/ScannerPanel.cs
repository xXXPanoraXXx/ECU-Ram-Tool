using System.ComponentModel;
using System.Globalization;
using EcuRamTool.Kwp;
using EcuRamTool.Profiles;
using EcuRamTool.Ram;

namespace EcuRamTool.Gui;

public sealed class ScannerPanel : UserControl
{
    private readonly KwpSession _session;
    private readonly Action<string> _log;

    private readonly GroupBox _scanBox = new();
    private readonly Label _valueLabel = new();
    private readonly TextBox _valueText = new();
    private readonly ComboBox _valueTypeBox = new();
    private readonly ComboBox _compareModeBox = new();
    private readonly Label _factorLabel = new();
    private readonly TextBox _factorText = new();
    private readonly Button _firstScanButton = new();
    private readonly Button _nextScanButton = new();
    private readonly Button _clearButton = new();

    private readonly GroupBox _memoryBox = new();
    private readonly ComboBox _rangeBox = new();
    private readonly Label _startLabel = new();
    private readonly Label _endLabel = new();
    private readonly TextBox _startAddressText = new();
    private readonly TextBox _endAddressText = new();

    private readonly Button _addWatchButton = new();
    private readonly Label _countLabel = new();
    private readonly Label _progressLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly DataGridView _grid = new();
    private readonly BindingList<ScanCandidate> _rows = new();
    private readonly ValueScanEngine _engine = new();

    private EcuProfile? _profile;
    private readonly List<MemoryRangeProfile> _ranges = new();

    public event Action<ScanCandidate>? AddToWatchRequested;

    public bool IsScanning { get; private set; }

    public ScannerPanel(KwpSession session, Action<string> log)
    {
        _session = session;
        _log = log;
        Dock = DockStyle.Fill;
        Padding = new Padding(8);

        BuildScanControls();
        BuildMemoryControls();
        BuildResultGrid();
        DarkTheme.ApplyChildren(this);

        Resize += (_, _) => LayoutControlsSafe();
        Load += (_, _) => LayoutControlsSafe();
    }

    private void BuildScanControls()
    {
        _scanBox.Text = "Scan";
        _scanBox.SetBounds(8, 8, 750, 74);
        _scanBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_scanBox);

        _valueLabel.Text = "Value:";
        _valueLabel.SetBounds(12, 31, 48, 22);
        _scanBox.Controls.Add(_valueLabel);

        _valueText.SetBounds(62, 28, 80, 26);
        _valueText.Text = "0";
        _scanBox.Controls.Add(_valueText);

        _valueTypeBox.SetBounds(150, 28, 170, 26);
        _valueTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _valueTypeBox.Items.AddRange(new object[]
        {
            ScanValueTypeText.BitMaskText,
            ScanValueTypeText.UInt8Text,
            ScanValueTypeText.UInt16Text
        });
        _valueTypeBox.SelectedIndex = 0;
        _valueTypeBox.SelectedIndexChanged += (_, _) => UpdateFactorVisibility();
        _scanBox.Controls.Add(_valueTypeBox);

        _compareModeBox.SetBounds(330, 28, 150, 26);
        _compareModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _compareModeBox.Items.AddRange(new object[]
        {
            ScanCompareModeText.ExactText,
            ScanCompareModeText.ApproxText,
            ScanCompareModeText.BelowText,
            ScanCompareModeText.AboveText
        });
        _compareModeBox.SelectedIndex = 0;
        _scanBox.Controls.Add(_compareModeBox);

        _factorLabel.Text = "Faktor:";
        _factorLabel.SetBounds(490, 31, 50, 22);
        _scanBox.Controls.Add(_factorLabel);

        _factorText.SetBounds(540, 28, 65, 26);
        _factorText.Text = "1";
        _scanBox.Controls.Add(_factorText);

        _firstScanButton.Text = "First Scan";
        _firstScanButton.SetBounds(615, 26, 95, 30);
        _firstScanButton.Click += async (_, _) => await ScanAsync(first: true);
        _scanBox.Controls.Add(_firstScanButton);

        _nextScanButton.Text = "Next Scan";
        _nextScanButton.SetBounds(715, 26, 95, 30);
        _nextScanButton.Click += async (_, _) => await ScanAsync(first: false);
        _scanBox.Controls.Add(_nextScanButton);

        UpdateFactorVisibility();
    }

    private void BuildMemoryControls()
    {
        _memoryBox.Text = "Memory Scan Optionen";
        _memoryBox.SetBounds(8, 90, 750, 86);
        _memoryBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_memoryBox);

        _rangeBox.SetBounds(12, 24, 320, 26);
        _rangeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _rangeBox.SelectedIndexChanged += (_, _) => LoadSelectedRangeIntoAddressBoxes();
        _memoryBox.Controls.Add(_rangeBox);

        _startLabel.Text = "Start RAM Address:";
        _startLabel.SetBounds(350, 27, 115, 22);
        _memoryBox.Controls.Add(_startLabel);

        _startAddressText.SetBounds(470, 24, 105, 26);
        _memoryBox.Controls.Add(_startAddressText);

        _endLabel.Text = "End RAM Address:";
        _endLabel.SetBounds(350, 55, 115, 22);
        _memoryBox.Controls.Add(_endLabel);

        _endAddressText.SetBounds(470, 52, 105, 26);
        _memoryBox.Controls.Add(_endAddressText);

        _clearButton.Text = "Clear";
        _clearButton.SetBounds(590, 23, 70, 30);
        _clearButton.Click += (_, _) => Clear();
        _memoryBox.Controls.Add(_clearButton);
    }

    private void BuildResultGrid()
    {
        _addWatchButton.Text = "Add selected to Watch";
        _addWatchButton.SetBounds(8, 184, 150, 30);
        _addWatchButton.Click += (_, _) => AddSelectedToWatch();
        Controls.Add(_addWatchButton);

        _countLabel.SetBounds(175, 190, 300, 22);
        _countLabel.Text = "Found: 0";
        Controls.Add(_countLabel);

        _progressLabel.Text = "Scan Progress:";
        _progressLabel.SetBounds(8, 222, 100, 22);
        Controls.Add(_progressLabel);

        _progressBar.SetBounds(115, 219, 635, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Value = 0;
        Controls.Add(_progressBar);

        _grid.SetBounds(8, 252, 750, 330);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = false;
        _grid.DataSource = _rows;
        _grid.RowHeadersVisible = false;
        AddColumn("AddressText", "Address", 135, true);
        AddColumn("ValueText", "Value", 170, true);
        AddColumn("TypeText", "Type", 150, true);
        AddColumn("Name", "Name", 240, false);
        Controls.Add(_grid);
    }


    private void LayoutControlsSafe()
    {
        int width = Math.Max(620, ClientSize.Width - 16);
        int height = Math.Max(420, ClientSize.Height - 16);

        _scanBox.SetBounds(8, 8, width, 74);
        int scanW = Math.Max(620, _scanBox.ClientSize.Width);

        _valueLabel.SetBounds(12, 31, 48, 22);
        _valueText.SetBounds(62, 28, 80, 26);
        _valueTypeBox.SetBounds(150, 28, 170, 26);
        _compareModeBox.SetBounds(330, 28, 150, 26);
        _factorLabel.SetBounds(490, 31, 50, 22);
        _factorText.SetBounds(540, 28, 65, 26);

        // Die Scan-Buttons werden immer rechts im Scan-Block gehalten.
        // Dadurch verschwindet Next Scan nicht mehr hinter dem Splitter/Watch-Bereich.
        int nextX = scanW - 110;
        int firstX = nextX - 105;
        if (firstX < 615)
        {
            firstX = 615;
            nextX = 715;
        }
        _firstScanButton.SetBounds(firstX, 26, 95, 30);
        _nextScanButton.SetBounds(nextX, 26, 95, 30);

        _memoryBox.SetBounds(8, 90, width, 86);
        int memoryW = Math.Max(620, _memoryBox.ClientSize.Width);
        int clearX = memoryW - 85;
        int startX = Math.Max(350, clearX - 250);
        int rangeW = Math.Max(260, Math.Min(340, startX - 24));

        _rangeBox.SetBounds(12, 24, rangeW, 26);
        _startLabel.SetBounds(startX, 27, 115, 22);
        _startAddressText.SetBounds(startX + 120, 24, 105, 26);
        _endLabel.SetBounds(startX, 55, 115, 22);
        _endAddressText.SetBounds(startX + 120, 52, 105, 26);
        _clearButton.SetBounds(clearX, 23, 70, 30);

        _addWatchButton.SetBounds(8, 184, 150, 30);
        _countLabel.SetBounds(175, 190, 300, 22);
        _progressLabel.SetBounds(8, 222, 100, 22);
        _progressBar.SetBounds(115, 219, Math.Max(120, width - 115), 22);
        _grid.SetBounds(8, 252, width, Math.Max(160, height - 260));
    }

    public void SetProfile(EcuProfile profile)
    {
        _profile = profile;
        _ranges.Clear();
        _ranges.AddRange(profile.MemoryRanges);
        _rangeBox.Items.Clear();
        foreach (MemoryRangeProfile range in _ranges)
            _rangeBox.Items.Add(range.Name);
        if (_rangeBox.Items.Count > 0)
            _rangeBox.SelectedIndex = 0;
    }

    private async Task ScanAsync(bool first)
    {
        if (_profile is null || _rangeBox.SelectedIndex < 0)
        {
            MessageBox.Show("Kein Profil/MemoryRange geladen.");
            return;
        }
        if (!_session.IsOpen || _session.ActiveTarget is null)
        {
            MessageBox.Show("Nicht verbunden.");
            return;
        }

        IsScanning = true;
        SetButtons(false);
        SetScanProgress(true);
        try
        {
            ScanValueType valueType = ScanValueTypeText.FromUiText(_valueTypeBox.SelectedItem?.ToString());
            double wanted = ParseDecimal(_valueText.Text, "Value");
            double factor = valueType == ScanValueType.BitMask ? 1.0 : ParseDecimal(_factorText.Text, "Faktor");
            ScanCompareMode compareMode = valueType == ScanValueType.BitMask
                ? ScanCompareMode.Exact
                : ScanCompareModeText.FromUiText(_compareModeBox.SelectedItem?.ToString());
            MemoryRangeProfile range = BuildRangeFromUi();

            var scanner = new RamRangeScanner(_session, _log);
            _log((first ? "First Scan" : "Next Scan") + $" type={ScanValueTypeText.ToUiText(valueType)}, compare={ScanCompareModeText.ToUiText(compareMode)}, value={wanted}, start={range.AddressPrefix}{range.Start}, end={range.AddressPrefix}{range.End}");

            RamSnapshot snapshot = await Task.Run(() => scanner.ReadRange(range));
            if (first)
                _engine.FirstScan(snapshot, valueType, wanted, factor, compareMode, range.ReadMethod ?? _profile.RamReadMethod, range.WriteMethod ?? _profile.RamWriteMethod);
            else
                _engine.NextScan(snapshot, valueType, wanted, factor, compareMode);

            RefreshRows();
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
        }
        catch (Exception ex)
        {
            _log("SCAN ERROR: " + ex.Message);
            MessageBox.Show(ex.Message);
        }
        finally
        {
            IsScanning = false;
            SetScanProgress(false);
            SetButtons(true);
        }
    }

    private MemoryRangeProfile BuildRangeFromUi()
    {
        MemoryRangeProfile baseRange = _ranges[_rangeBox.SelectedIndex];
        int start = ParseAddress24(_startAddressText.Text, baseRange);
        int end = ParseAddress24(_endAddressText.Text, baseRange);

        if (end < start)
            throw new InvalidOperationException("End RAM Address ist kleiner als Start RAM Address.");

        return new MemoryRangeProfile
        {
            Name = baseRange.Name,
            AddressPrefix = "0x" + ((start >> 16) & 0xFF).ToString("X2"),
            Start = "0x" + (start & 0xFFFF).ToString("X4"),
            End = "0x" + (end & 0xFFFF).ToString("X4"),
            ReadSize = baseRange.ReadSize,
            ReadMethod = baseRange.ReadMethod,
            WriteMethod = baseRange.WriteMethod,
            Mode = baseRange.Mode
        };
    }

    private static int ParseAddress24(string text, MemoryRangeProfile baseRange)
    {
        string cleaned = text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (cleaned.Length == 0)
            throw new FormatException("RAM-Adresse fehlt.");

        int value = int.Parse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (cleaned.Length <= 4)
        {
            int prefix = HexUtil.ParseInt(baseRange.AddressPrefix) & 0xFF;
            value = (prefix << 16) | (value & 0xFFFF);
        }

        return value & 0xFFFFFF;
    }

    private static double ParseDecimal(string text, string label)
    {
        text = text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException(label + " ist keine gültige Zahl.");
        return value;
    }

    private void LoadSelectedRangeIntoAddressBoxes()
    {
        if (_rangeBox.SelectedIndex < 0 || _rangeBox.SelectedIndex >= _ranges.Count)
            return;

        MemoryRangeProfile range = _ranges[_rangeBox.SelectedIndex];
        int prefix = HexUtil.ParseInt(range.AddressPrefix) & 0xFF;
        int start = HexUtil.ParseInt(range.Start) & 0xFFFF;
        int end = HexUtil.ParseInt(range.End) & 0xFFFF;

        _startAddressText.Text = "0x" + ((prefix << 16) | start).ToString("X6");
        _endAddressText.Text = "0x" + ((prefix << 16) | end).ToString("X6");
    }

    private void UpdateFactorVisibility()
    {
        bool visible = ScanValueTypeText.FromUiText(_valueTypeBox.SelectedItem?.ToString()) != ScanValueType.BitMask;
        _compareModeBox.Visible = visible;
        _factorLabel.Visible = visible;
        _factorText.Visible = visible;
    }

    private void Clear()
    {
        _engine.Clear();
        RefreshRows();
    }

    private void AddSelectedToWatch()
    {
        if (_grid.CurrentRow?.DataBoundItem is ScanCandidate c)
            AddToWatchRequested?.Invoke(c);
    }

    private void RefreshRows()
    {
        _rows.Clear();
        foreach (ScanCandidate c in _engine.Candidates)
            _rows.Add(c);
        _countLabel.Text = "Found: " + _rows.Count;
    }

    private void SetButtons(bool enabled)
    {
        _firstScanButton.Enabled = enabled;
        _nextScanButton.Enabled = enabled;
        _clearButton.Enabled = enabled;
        _addWatchButton.Enabled = enabled;
    }

    private void SetScanProgress(bool active)
    {
        if (active)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 25;
        }
        else
        {
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Style = ProgressBarStyle.Continuous;
        }
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
}

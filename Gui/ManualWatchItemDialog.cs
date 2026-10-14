using System.Globalization;
using EcuRamTool.Kwp;
using EcuRamTool.Profiles;
using EcuRamTool.Ram;

namespace EcuRamTool.Gui;

public sealed class ManualWatchItemDialog : Form
{
    private readonly ComboBox _typeBox = new();
    private readonly TextBox _nameText = new();
    private readonly TextBox _unitText = new();
    private readonly TextBox _addressText = new();
    private readonly TextBox _maskText = new();
    private readonly TextBox _factorText = new();
    private readonly ComboBox _readMethodBox = new();
    private readonly Label _maskLabel = new();
    private readonly Label _factorLabel = new();
    private readonly Button _addFromEcuButton = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private readonly WatchItem? _existingItem;
    private string? _selectedEcuFilePath;

    public WatchItem? CreatedItem { get; private set; }
    public List<WatchItem> ImportedItems { get; private set; } = new();
    public string? SelectedEcuFilePath => _selectedEcuFilePath;

    public ManualWatchItemDialog(EcuProfile? profile, WatchItem? existingItem = null, string? ecuFilePath = null)
    {
        _existingItem = existingItem;
        _selectedEcuFilePath = ecuFilePath;

        Text = existingItem is null ? "Adresse manuell zur Watchlist hinzufügen" : "Watchlist-Eintrag bearbeiten";
        Width = 430;
        Height = existingItem is null ? 360 : 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        BuildLayout(profile);

        if (existingItem is not null)
            LoadExistingItem(existingItem);

        UpdateVisibility();
        DarkTheme.Apply(this);
    }

    private void BuildLayout(EcuProfile? profile)
    {
        int labelX = 14;
        int editX = 120;
        int y = 18;

        Controls.Add(MakeLabel("Typ:", labelX, y));
        _typeBox.SetBounds(editX, y - 3, 260, 26);
        _typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeBox.Items.AddRange(new object[]
        {
            ScanValueTypeText.BitMaskText,
            ScanValueTypeText.UInt8Text,
            ScanValueTypeText.UInt16Text
        });
        _typeBox.SelectedIndex = 0;
        _typeBox.SelectedIndexChanged += (_, _) => UpdateVisibility();
        Controls.Add(_typeBox);

        y += 34;
        Controls.Add(MakeLabel("Name:", labelX, y));
        _nameText.SetBounds(editX, y - 3, 260, 26);
        _nameText.PlaceholderText = "z.B. Kupplung";
        Controls.Add(_nameText);

        y += 34;
        Controls.Add(MakeLabel("Einheit:", labelX, y));
        _unitText.SetBounds(editX, y - 3, 260, 26);
        _unitText.PlaceholderText = "z.B. rpm, Volt, °C";
        Controls.Add(_unitText);

        y += 34;
        Controls.Add(MakeLabel("RAM Adresse:", labelX, y));
        _addressText.SetBounds(editX, y - 3, 260, 26);
        _addressText.PlaceholderText = "z.B. 0x00FD4C oder 0x380A0A";
        Controls.Add(_addressText);

        y += 34;
        _maskLabel.Text = "Mask:";
        _maskLabel.SetBounds(labelX, y, 100, 22);
        Controls.Add(_maskLabel);
        _maskText.SetBounds(editX, y - 3, 260, 26);
        _maskText.Text = "0x01";
        _maskText.PlaceholderText = "z.B. 0x10 oder 0x8000";
        Controls.Add(_maskText);

        y += 34;
        _factorLabel.Text = "Faktor:";
        _factorLabel.SetBounds(labelX, y, 100, 22);
        Controls.Add(_factorLabel);
        _factorText.SetBounds(editX, y - 3, 260, 26);
        _factorText.Text = "1";
        Controls.Add(_factorText);

        y += 34;
        Controls.Add(MakeLabel("Read:", labelX, y));
        _readMethodBox.SetBounds(editX, y - 3, 260, 26);
        _readMethodBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _readMethodBox.Items.Add("Profil-Standard");
        _readMethodBox.Items.Add("23");
        _readMethodBox.Items.Add("DDLI21");
        _readMethodBox.SelectedIndex = 0;
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.RamReadMethod))
            _readMethodBox.Items[0] = "Profil-Standard (" + profile.RamReadMethod + ")";
        Controls.Add(_readMethodBox);

        y += 40;
        if (_existingItem is null)
        {
            _addFromEcuButton.Text = "Add from .ecu...";
            _addFromEcuButton.SetBounds(14, y, 130, 30);
            _addFromEcuButton.Click += (_, _) => AddFromEcu();
            Controls.Add(_addFromEcuButton);
        }

        _okButton.Text = "OK";
        _okButton.SetBounds(220, y, 75, 30);
        _okButton.Click += (_, _) => TryAccept();
        Controls.Add(_okButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.SetBounds(305, y, 75, 30);
        _cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }


    private void AddFromEcu()
    {
        using var dialog = new EcuMeasurementImportDialog(_selectedEcuFilePath);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        ImportedItems = dialog.SelectedWatchItems;
        _selectedEcuFilePath = dialog.EcuFilePath;
        if (ImportedItems.Count > 0)
            DialogResult = DialogResult.OK;
    }

    private void LoadExistingItem(WatchItem item)
    {
        _typeBox.SelectedItem = ScanValueTypeText.ToUiText(item.ValueType);
        _nameText.Text = item.Name;
        _unitText.Text = item.Unit;
        _addressText.Text = "0x" + item.Address24.ToString("X6");
        _maskText.Text = "0x" + Math.Max(1, item.Mask).ToString(item.Mask > 0xFF ? "X4" : "X2");
        _factorText.Text = item.Factor.ToString(CultureInfo.InvariantCulture);

        string method = item.ReadMethod.Trim();
        if (string.Equals(method, "23", StringComparison.OrdinalIgnoreCase))
            _readMethodBox.SelectedIndex = 1;
        else if (string.Equals(method, "DDLI21", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "2C21", StringComparison.OrdinalIgnoreCase))
            _readMethodBox.SelectedIndex = 2;
        else
            _readMethodBox.SelectedIndex = 0;
    }

    private static Label MakeLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Left = x,
            Top = y,
            Width = 100,
            Height = 22
        };
    }

    private void UpdateVisibility()
    {
        ScanValueType type = ScanValueTypeText.FromUiText(_typeBox.SelectedItem?.ToString());
        bool isBitMask = type == ScanValueType.BitMask;
        bool isNumeric = type is ScanValueType.UInt8 or ScanValueType.UInt16;

        _maskLabel.Visible = isBitMask;
        _maskText.Visible = isBitMask;
        _factorLabel.Visible = isNumeric;
        _factorText.Visible = isNumeric;
    }

    private void TryAccept()
    {
        try
        {
            ScanValueType type = ScanValueTypeText.FromUiText(_typeBox.SelectedItem?.ToString());
            int address = ParseAddress(_addressText.Text);
            double factor = 1.0;
            int mask = 0;
            int bit = -1;

            if (type == ScanValueType.BitMask)
            {
                mask = ParseMask(_maskText.Text);
                bit = TryGetSingleBitIndex(mask);
            }
            else
            {
                factor = ParseFactor(_factorText.Text);
            }

            CreatedItem = new WatchItem
            {
                Address24 = address,
                ValueType = type,
                Bit = bit,
                MaskValue = mask,
                Factor = factor,
                Name = _nameText.Text.Trim(),
                Unit = _unitText.Text.Trim(),
                ReadMethod = SelectedReadMethod()
            };

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ungültige Eingabe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string SelectedReadMethod()
    {
        int index = _readMethodBox.SelectedIndex;
        return index switch
        {
            1 => "23",
            2 => "DDLI21",
            _ => string.Empty
        };
    }

    private static int ParseAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Bitte eine RAM-Adresse eingeben.");

        int address = HexUtil.ParseInt(text);
        if (address < 0 || address > 0xFFFFFF)
            throw new FormatException("RAM-Adresse muss im 24-bit Bereich 0x000000-0xFFFFFF liegen.");

        return address;
    }

    private static int ParseMask(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Bitte eine Maske eingeben, z.B. 0x10 oder 0x8000.");

        int mask = HexUtil.ParseInt(text);
        if (mask <= 0 || mask > 0xFFFF)
            throw new FormatException("Mask muss zwischen 0x0001 und 0xFFFF liegen.");

        return mask;
    }

    private static double ParseFactor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1.0;

        string normalized = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException("Faktor ist keine gültige Zahl.");

        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            throw new FormatException("Faktor muss größer 0 sein.");

        return value;
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

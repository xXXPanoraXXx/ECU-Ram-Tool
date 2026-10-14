using EcuRamTool.Ram;

namespace EcuRamTool.Gui;

public sealed class WriteWatchItemDialog : Form
{
    private readonly WatchItem _item;
    private readonly TextBox _valueText = new();
    private readonly CheckBox _forceCheck = new();
    private readonly NumericUpDown _intervalBox = new();
    private readonly Button _writeButton = new();
    private readonly Button _cancelButton = new();

    public string ValueText => _valueText.Text.Trim();
    public bool ForceWriteEnabled => _forceCheck.Checked;
    public int IntervalMs => (int)_intervalBox.Value;

    public WriteWatchItemDialog(WatchItem item)
    {
        _item = item;
        Text = "Write this address";
        Width = 430;
        Height = 240;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        DarkTheme.Apply(this);
    }

    private void BuildLayout()
    {
        int labelX = 14;
        int editX = 150;
        int y = 18;

        Controls.Add(MakeLabel("Adresse:", labelX, y));
        Controls.Add(MakeValueLabel(_item.AddressText, editX, y, 240));

        y += 30;
        Controls.Add(MakeLabel("Typ:", labelX, y));
        Controls.Add(MakeValueLabel(_item.TypeText, editX, y, 240));

        y += 30;
        Controls.Add(MakeLabel("Aktueller Value:", labelX, y));
        string current = string.IsNullOrWhiteSpace(_item.UnitText)
            ? _item.ValueText
            : _item.ValueText + " " + _item.UnitText;
        Controls.Add(MakeValueLabel(current, editX, y, 240));

        y += 34;
        Controls.Add(MakeLabel(_item.ValueType == ScanValueType.BitMask ? "Neuer Wert 0/1:" : "Neuer Value:", labelX, y));
        _valueText.SetBounds(editX, y - 3, 240, 26);
        _valueText.Text = _item.ValueType == ScanValueType.BitMask ? (_item.LastRawValue != 0 ? "1" : "0") : _item.ValueText;
        Controls.Add(_valueText);

        y += 34;
        _forceCheck.Text = "Force write this address";
        _forceCheck.SetBounds(editX, y - 2, 210, 24);
        _forceCheck.Checked = _item.ForceWriteEnabled;
        Controls.Add(_forceCheck);

        y += 30;
        Controls.Add(MakeLabel("Intervall ms:", labelX, y));
        _intervalBox.SetBounds(editX, y - 3, 90, 26);
        _intervalBox.Minimum = 0;
        _intervalBox.Maximum = 100000;
        _intervalBox.Increment = 10;
        int defaultInterval = _item.ForceWriteEnabled ? _item.ForceWriteIntervalMs : 0;
        _intervalBox.Value = Math.Max(0, Math.Min(100000, defaultInterval));
        Controls.Add(_intervalBox);
        Controls.Add(MakeValueLabel("0 = so schnell wie Scheduler erlaubt", editX + 98, y, 210));

        y += 42;
        _writeButton.Text = "Write";
        _writeButton.SetBounds(235, y, 75, 30);
        _writeButton.Click += (_, _) => DialogResult = DialogResult.OK;
        Controls.Add(_writeButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.SetBounds(315, y, 75, 30);
        _cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        AcceptButton = _writeButton;
        CancelButton = _cancelButton;
    }

    private static Label MakeLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Left = x,
            Top = y,
            Width = 130,
            Height = 22
        };
    }

    private static Label MakeValueLabel(string text, int x, int y, int width)
    {
        return new Label
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = 22
        };
    }
}

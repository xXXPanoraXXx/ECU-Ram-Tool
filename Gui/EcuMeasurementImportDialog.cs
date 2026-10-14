using System.Drawing;
using System.ComponentModel;
using EcuRamTool.Ram;

namespace EcuRamTool.Gui;

public sealed class EcuMeasurementImportDialog : Form
{
    private readonly TextBox _searchText = new();
    private readonly Button _chooseFileButton = new();
    private readonly Label _fileLabel = new();
    private readonly DataGridView _grid = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private readonly BindingList<EcuMeasurement> _visibleItems = new();
    private List<EcuMeasurement> _allItems = new();
    private string? _ecuFilePath;
    private bool _loadedOnce;

    public string? EcuFilePath => _ecuFilePath;
    public List<WatchItem> SelectedWatchItems { get; private set; } = new();

    public EcuMeasurementImportDialog(string? initialEcuFilePath = null)
    {
        _ecuFilePath = File.Exists(initialEcuFilePath) ? initialEcuFilePath : null;

        Text = "Adressen aus .ecu hinzufügen";
        Width = 920;
        Height = 640;
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        BuildLayout();
        DarkTheme.Apply(this);
        StyleGrid();

        Shown += (_, _) =>
        {
            if (_loadedOnce)
                return;

            _loadedOnce = true;

            if (!string.IsNullOrWhiteSpace(_ecuFilePath))
                LoadEcuFile(_ecuFilePath);
            else
                ChooseEcuFile();
        };
    }

    private void BuildLayout()
    {
        SuspendLayout();

        _chooseFileButton.Text = "Andere .ecu...";
        _chooseFileButton.SetBounds(12, 12, 110, 30);
        _chooseFileButton.Click += (_, _) => ChooseEcuFile();
        Controls.Add(_chooseFileButton);

        _fileLabel.Text = ".ecu: <nicht geladen>";
        _fileLabel.SetBounds(132, 17, 730, 22);
        _fileLabel.AutoEllipsis = true;
        Controls.Add(_fileLabel);

        _searchText.SetBounds(12, 52, 870, 28);
        _searchText.PlaceholderText = "Suchen nach Name, Alias, Adresse oder Kommentar, z.B. B_kuppl";
        _searchText.TextChanged += (_, _) => ApplyFilter();
        Controls.Add(_searchText);

        _grid.SetBounds(12, 90, 870, 455);
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _visibleItems;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is EcuMeasurement item)
                item.Selected = !item.Selected;
            _grid.Refresh();
        };

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(EcuMeasurement.Selected),
            HeaderText = "Use",
            Width = 45
        });
        AddTextColumn(nameof(EcuMeasurement.Name), "Name", 125);
        AddTextColumn(nameof(EcuMeasurement.AliasText), "Alias", 185);
        AddTextColumn(nameof(EcuMeasurement.AddressText), "Adresse", 95);
        AddTextColumn(nameof(EcuMeasurement.Comment), "Comment", 390);
        Controls.Add(_grid);

        _okButton.Text = "OK";
        _okButton.SetBounds(710, 558, 80, 30);
        _okButton.Click += (_, _) => AcceptSelection();
        Controls.Add(_okButton);

        _cancelButton.Text = "Cancel";
        _cancelButton.SetBounds(802, 558, 80, 30);
        _cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Resize += (_, _) => LayoutControlsSafe();
        ResumeLayout(false);
    }

    private void LayoutControlsSafe()
    {
        int w = Math.Max(740, ClientSize.Width);
        int h = Math.Max(440, ClientSize.Height);

        _fileLabel.SetBounds(132, 17, Math.Max(240, w - 150), 22);
        _searchText.SetBounds(12, 52, Math.Max(240, w - 24), 28);
        _grid.SetBounds(12, 90, Math.Max(240, w - 24), Math.Max(200, h - 145));
        _okButton.SetBounds(w - 190, h - 42, 80, 30);
        _cancelButton.SetBounds(w - 98, h - 42, 80, 30);
    }

    private void AddTextColumn(string property, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            Width = width,
            ReadOnly = true
        });
    }

    private void ChooseEcuFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = ".ecu auswählen",
            Filter = "ME7Logger ECU Datei (*.ecu)|*.ecu|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(_ecuFilePath) && File.Exists(_ecuFilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_ecuFilePath);
            dialog.FileName = Path.GetFileName(_ecuFilePath);
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        LoadEcuFile(dialog.FileName);
    }

    private void LoadEcuFile(string path)
    {
        try
        {
            _allItems = EcuMeasurementParser.LoadFromFile(path);
            _ecuFilePath = path;
            _fileLabel.Text = ".ecu: " + path + $"  ({_allItems.Count} Variablen)";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, ".ecu laden", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyFilter()
    {
        string q = _searchText.Text.Trim();
        IEnumerable<EcuMeasurement> query = _allItems;

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x =>
                ContainsIgnoreCase(x.Name, q) ||
                ContainsIgnoreCase(x.Alias, q) ||
                ContainsIgnoreCase(x.AddressText, q) ||
                ContainsIgnoreCase(x.Comment, q));
        }

        _visibleItems.RaiseListChangedEvents = false;
        _visibleItems.Clear();
        foreach (EcuMeasurement item in query.Take(2500))
            _visibleItems.Add(item);
        _visibleItems.RaiseListChangedEvents = true;
        _visibleItems.ResetBindings();
    }

    private static bool ContainsIgnoreCase(string? text, string value)
    {
        return !string.IsNullOrEmpty(text) && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void AcceptSelection()
    {
        _grid.EndEdit();

        var selected = _allItems.Where(x => x.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Bitte mindestens eine Adresse anhaken.", "Add from .ecu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedWatchItems = selected.Select(x => x.ToWatchItem()).ToList();
        DialogResult = DialogResult.OK;
    }

    private void StyleGrid()
    {
        DarkTheme.StyleGrid(_grid);
    }
}

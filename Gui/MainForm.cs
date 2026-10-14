using System.Drawing;
using EcuRamTool.Kwp;
using EcuRamTool.Storage;

namespace EcuRamTool.Gui;

public sealed class MainForm : Form
{
    private readonly KwpSession _session;
    private readonly LogBox _logBox = new();
    private readonly ConnectionPanel _connectionPanel;
    private readonly ManualCommandPanel _manualPanel;
    private readonly ScannerPanel _scannerPanel;
    private readonly WatchPanel _watchPanel;
    private readonly SplitContainer _mainSplit = new();
    private readonly Panel _debugPanel = new();
    private readonly TableLayoutPanel _root = new();
    private readonly MenuStrip _menu = new();

    private readonly ToolStripMenuItem _saveConfigMenuItem = new("Save Config");
    private readonly ToolStripMenuItem _loadConfigMenuItem = new("Load Config");
    private readonly ToolStripMenuItem _saveConfigAsMenuItem = new("Save Config As...");
    private readonly ToolStripMenuItem _debugConsoleMenuItem = new("Debug Console anzeigen");

    private string? _currentConfigPath;

    public MainForm()
    {
        Text = "ECU RAM Tool v0.1.19";
        Width = 1220;
        Height = 760;
        MinimumSize = new Size(1050, 620);
        StartPosition = FormStartPosition.CenterScreen;

        _session = new KwpSession(Log);

        _connectionPanel = new ConnectionPanel(_session, Log);
        _manualPanel = new ManualCommandPanel(_session, Log);
        _scannerPanel = new ScannerPanel(_session, Log);
        _watchPanel = new WatchPanel(_session, Log);

        _connectionPanel.ProfileLoaded += profile =>
        {
            _scannerPanel.SetProfile(profile);
            _watchPanel.SetProfile(profile);
        };
        _scannerPanel.AddToWatchRequested += c => _watchPanel.AddCandidate(c);
        _watchPanel.SetExternalBusyProvider(() => _scannerPanel.IsScanning);

        BuildMenu();
        BuildLayout();
        DarkTheme.Apply(this);
        StyleMenu();

        // ConnectionPanel lädt Profile bereits im Konstruktor. Dadurch kann das erste
        // ProfileLoaded-Event vor der Subscription kommen. Deshalb hier nochmal sauber
        // an Scanner/Watch übergeben, falls bereits ein Profil aktiv ist.
        if (_connectionPanel.ActiveProfile is not null)
        {
            _scannerPanel.SetProfile(_connectionPanel.ActiveProfile);
            _watchPanel.SetProfile(_connectionPanel.ActiveProfile);
        }

        Log("ECU RAM Tool ready. Debug Console ist unter Optionen versteckt.");

        Shown += (_, _) => TryLoadLastConfigAtStartup();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        _root.Dock = DockStyle.Fill;
        _root.ColumnCount = 1;
        _root.RowCount = 4;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        Controls.Add(_root);

        _menu.Dock = DockStyle.Fill;
        _root.Controls.Add(_menu, 0, 0);

        _connectionPanel.Dock = DockStyle.Fill;
        _root.Controls.Add(_connectionPanel, 0, 1);

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.Panel1.Controls.Add(_scannerPanel);
        _mainSplit.Panel2.Controls.Add(_watchPanel);
        _root.Controls.Add(_mainSplit, 0, 2);

        _debugPanel.Dock = DockStyle.Fill;
        _debugPanel.Visible = false;
        _debugPanel.Controls.Add(_logBox);
        _debugPanel.Controls.Add(_manualPanel);
        _manualPanel.Dock = DockStyle.Top;
        _logBox.Dock = DockStyle.Fill;
        _root.Controls.Add(_debugPanel, 0, 3);

        ResumeLayout(true);
    }

    private void BuildMenu()
    {
        var fileRoot = new ToolStripMenuItem("File");
        _saveConfigMenuItem.Click += (_, _) => SaveConfig();
        _loadConfigMenuItem.Click += (_, _) => LoadConfig();
        _saveConfigAsMenuItem.Click += (_, _) => SaveConfigAs();
        fileRoot.DropDownItems.Add(_saveConfigMenuItem);
        fileRoot.DropDownItems.Add(_loadConfigMenuItem);
        fileRoot.DropDownItems.Add(new ToolStripSeparator());
        fileRoot.DropDownItems.Add(_saveConfigAsMenuItem);

        var optionsRoot = new ToolStripMenuItem("Optionen");
        _debugConsoleMenuItem.CheckOnClick = true;
        _debugConsoleMenuItem.CheckedChanged += (_, _) => SetDebugConsoleVisible(_debugConsoleMenuItem.Checked);
        optionsRoot.DropDownItems.Add(_debugConsoleMenuItem);

        _menu.Items.Add(fileRoot);
        _menu.Items.Add(optionsRoot);
        MainMenuStrip = _menu;
    }

    private void SaveConfig()
    {
        if (string.IsNullOrWhiteSpace(_currentConfigPath))
        {
            SaveConfigAs();
            return;
        }

        try
        {
            ToolConfigStore.Save(_currentConfigPath, _watchPanel.GetItemsSnapshot(), _watchPanel.EcuFilePath, _watchPanel.StopReading, _watchPanel.StopForceWrite);
            RecentConfigStore.SaveLastConfigPath(_currentConfigPath);
            Log("Config saved: " + _currentConfigPath);
        }
        catch (Exception ex)
        {
            Log("CONFIG SAVE ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Save Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveConfigAs()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save Config As",
            Filter = "ECU RAM Tool Config (*.json)|*.json|Alle Dateien (*.*)|*.*",
            AddExtension = true,
            DefaultExt = "json",
            FileName = SuggestedConfigFileName()
        };

        if (!string.IsNullOrWhiteSpace(_currentConfigPath))
            dialog.FileName = Path.GetFileName(_currentConfigPath);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _currentConfigPath = dialog.FileName;
        SaveConfig();
    }

    private void LoadConfig()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load Config",
            Filter = "ECU RAM Tool Config (*.json)|*.json|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        LoadConfigFromPath(dialog.FileName, showErrors: true);
    }

    private void TryLoadLastConfigAtStartup()
    {
        string? path = RecentConfigStore.LoadLastConfigPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        LoadConfigFromPath(path, showErrors: false);
    }

    private void LoadConfigFromPath(string path, bool showErrors)
    {
        try
        {
            ToolConfig config = ToolConfigStore.Load(path);
            _watchPanel.ReplaceItems(config.WatchItems);
            _watchPanel.EcuFilePath = config.EcuFilePath;
            _watchPanel.StopReading = config.StopReading;
            _watchPanel.StopForceWrite = config.StopForceWrite;
            _currentConfigPath = path;
            RecentConfigStore.SaveLastConfigPath(path);

            string ecuInfo = string.IsNullOrWhiteSpace(config.EcuFilePath)
                ? string.Empty
                : " | .ecu: " + config.EcuFilePath;

            Log($"Config loaded: {path} ({config.WatchItems.Count} watch items){ecuInfo}");
        }
        catch (Exception ex)
        {
            Log("CONFIG LOAD ERROR: " + ex.Message);
            if (showErrors)
                MessageBox.Show(this, ex.Message, "Load Config", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SuggestedConfigFileName()
    {
        return "ecu_ram_watch_config.json";
    }

    private void SetDebugConsoleVisible(bool visible)
    {
        _debugPanel.Visible = visible;
        if (_root.RowStyles.Count >= 4)
            _root.RowStyles[3].Height = visible ? 220 : 0;
        _root.PerformLayout();
    }

    private void StyleMenu()
    {
        _menu.BackColor = DarkTheme.PanelColor;
        _menu.ForeColor = DarkTheme.Fore;

        foreach (ToolStripItem item in _menu.Items)
            StyleMenuItemRecursive(item);
    }

    private static void StyleMenuItemRecursive(ToolStripItem item)
    {
        item.BackColor = DarkTheme.PanelColor;
        item.ForeColor = DarkTheme.Fore;

        if (item is ToolStripMenuItem menuItem)
        {
            foreach (ToolStripItem child in menuItem.DropDownItems)
                StyleMenuItemRecursive(child);
        }
    }

    private void Log(string message) => _logBox.Add(message);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _session.Dispose();
        base.OnFormClosing(e);
    }
}

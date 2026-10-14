using System.IO.Ports;
using EcuRamTool.Kwp;
using EcuRamTool.Profiles;

namespace EcuRamTool.Gui;

public sealed class ConnectionPanel : UserControl
{
    private readonly ComboBox _portBox = new();
    private readonly ComboBox _profileBox = new();
    private readonly Button _refreshButton = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly Label _statusLabel = new();
    private readonly Action<string> _log;

    private readonly List<string> _profileFiles = new();

    public KwpSession Session { get; }
    public EcuProfile? ActiveProfile { get; private set; }

    public event Action<EcuProfile>? ProfileLoaded;

    public ConnectionPanel(KwpSession session, Action<string> log)
    {
        Session = session;
        _log = log;
        Height = 48;
        Dock = DockStyle.Top;

        _portBox.SetBounds(10, 12, 100, 24);
        Controls.Add(_portBox);

        _refreshButton.Text = "Refresh";
        _refreshButton.SetBounds(120, 10, 80, 28);
        _refreshButton.Click += (_, _) => LoadPorts();
        Controls.Add(_refreshButton);

        _profileBox.SetBounds(210, 12, 270, 24);
        _profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileBox.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        Controls.Add(_profileBox);

        _connectButton.Text = "Connect";
        _connectButton.SetBounds(490, 10, 100, 28);
        _connectButton.Click += async (_, _) => await ConnectAsync();
        Controls.Add(_connectButton);

        _disconnectButton.Text = "Disconnect";
        _disconnectButton.SetBounds(600, 10, 100, 28);
        _disconnectButton.Click += (_, _) => Disconnect();
        Controls.Add(_disconnectButton);

        _statusLabel.SetBounds(720, 15, 350, 24);
        _statusLabel.Text = "Status: not connected";
        Controls.Add(_statusLabel);

        Session.SessionStatusChanged += alive =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateStatus(alive)));
                return;
            }
            UpdateStatus(alive);
        };

        LoadPorts();
        LoadProfiles();
        DarkTheme.ApplyChildren(this);
    }

    private void LoadPorts()
    {
        string old = _portBox.Text;
        _portBox.Items.Clear();
        string[] ports = SerialPort.GetPortNames().OrderBy(x => x).ToArray();
        _portBox.Items.AddRange(ports);
        if (ports.Length > 0)
        {
            int com3 = Array.IndexOf(ports, "COM3");
            int oldIndex = Array.IndexOf(ports, old);
            _portBox.SelectedIndex = com3 >= 0 ? com3 : oldIndex >= 0 ? oldIndex : 0;
        }
        _log("Ports: " + (ports.Length == 0 ? "<none>" : string.Join(", ", ports)));
    }

    private void LoadProfiles()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Profiles");
        _profileFiles.Clear();
        _profileFiles.AddRange(ProfileLoader.FindProfileFiles(dir));
        _profileBox.Items.Clear();
        foreach (string file in _profileFiles)
            _profileBox.Items.Add(Path.GetFileNameWithoutExtension(file));
        if (_profileBox.Items.Count > 0)
            _profileBox.SelectedIndex = 0;
    }

    private void LoadSelectedProfile()
    {
        if (_profileBox.SelectedIndex < 0)
            return;
        string file = _profileFiles[_profileBox.SelectedIndex];
        ActiveProfile = ProfileLoader.Load(file);
        _log("Profile loaded: " + ActiveProfile.Name);
        ProfileLoaded?.Invoke(ActiveProfile);
    }

    private async Task ConnectAsync()
    {
        if (_portBox.SelectedItem is null)
        {
            MessageBox.Show("Bitte COM-Port auswählen.");
            return;
        }
        if (ActiveProfile is null)
        {
            MessageBox.Show("Bitte Profil auswählen.");
            return;
        }

        _connectButton.Enabled = false;
        _statusLabel.Text = "Status: connecting...";
        try
        {
            string port = _portBox.SelectedItem.ToString()!;
            bool ok = await Task.Run(() => Session.Connect(port, ActiveProfile));
            _statusLabel.Text = ok ? "Status: connected" : "Status: failed";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Status: error";
            _log("CONNECT ERROR: " + ex.Message);
            Disconnect();
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }

    private void Disconnect()
    {
        Session.Disconnect();
        _statusLabel.Text = "Status: not connected";
    }

    private void UpdateStatus(bool alive)
    {
        // Status soll sofort von jeder echten ECU-Antwort kommen, nicht nur vom TesterPresent.
        // Keine Antwort im aktuellen Antwortfenster = not connected.
        _statusLabel.Text = alive && Session.ActiveTarget is not null
            ? "Status: connected"
            : "Status: not connected";
    }
}

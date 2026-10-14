using EcuRamTool.Kwp;

namespace EcuRamTool.Gui;

public sealed class ManualCommandPanel : UserControl
{
    private readonly KwpSession _session;
    private readonly Action<string> _log;
    private readonly TextBox _commandBox = new();
    private readonly Button _sendButton = new();

    public ManualCommandPanel(KwpSession session, Action<string> log)
    {
        _session = session;
        _log = log;
        Height = 45;
        Dock = DockStyle.Top;

        _commandBox.SetBounds(10, 9, 780, 26);
        _commandBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _commandBox.Font = new System.Drawing.Font("Consolas", 10);
        _commandBox.PlaceholderText = "Manueller Hex-Befehl, z.B. 81 11 F1 3E C1";
        _commandBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendAsync();
            }
        };
        Controls.Add(_commandBox);

        _sendButton.Text = "Send";
        _sendButton.SetBounds(805, 8, 90, 28);
        _sendButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _sendButton.Click += async (_, _) => await SendAsync();
        Controls.Add(_sendButton);
        DarkTheme.ApplyChildren(this);
    }

    private async Task SendAsync()
    {
        if (!_session.IsOpen)
        {
            _log("Not connected.");
            return;
        }

        string text = _commandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            _sendButton.Enabled = false;
            byte[] tx = HexUtil.ParseBytes(text);
            await Task.Run(() => _session.SendUserCommand(tx));
            _commandBox.SelectAll();
            _commandBox.Focus();
        }
        catch (Exception ex)
        {
            _log("MANUAL SEND ERROR: " + ex.Message);
        }
        finally
        {
            _sendButton.Enabled = true;
        }
    }
}

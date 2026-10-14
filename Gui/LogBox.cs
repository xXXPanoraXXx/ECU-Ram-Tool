namespace EcuRamTool.Gui;

public sealed class LogBox : TextBox
{
    public LogBox()
    {
        Multiline = true;
        ScrollBars = ScrollBars.Vertical;
        ReadOnly = true;
        Font = new System.Drawing.Font("Consolas", 10);
        BackColor = DarkTheme.Input;
        ForeColor = DarkTheme.Fore;
        BorderStyle = BorderStyle.FixedSingle;
    }

    public void Add(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Add), message);
            return;
        }
        AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
}

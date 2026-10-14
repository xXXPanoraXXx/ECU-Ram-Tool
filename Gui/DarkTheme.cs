using System.Drawing;

namespace EcuRamTool.Gui;

internal static class DarkTheme
{
    public static readonly Color Back = Color.FromArgb(24, 24, 24);
    public static readonly Color PanelColor = Color.FromArgb(32, 32, 32);
    public static readonly Color Panel2Color = Color.FromArgb(38, 38, 38);
    public static readonly Color Grid = Color.FromArgb(28, 28, 28);
    public static readonly Color GridAlt = Color.FromArgb(34, 34, 34);
    public static readonly Color Fore = Color.FromArgb(230, 230, 230);
    public static readonly Color Muted = Color.FromArgb(170, 170, 170);
    public static readonly Color Border = Color.FromArgb(70, 70, 70);
    public static readonly Color Accent = Color.FromArgb(0, 122, 204);
    public static readonly Color Button = Color.FromArgb(45, 45, 48);
    public static readonly Color Input = Color.FromArgb(30, 30, 30);

    public static void Apply(Control root)
    {
        root.BackColor = Back;
        root.ForeColor = Fore;
        ApplyChildren(root);
    }

    public static void ApplyChildren(Control parent)
    {
        foreach (Control c in parent.Controls)
            ApplyControl(c);
    }

    public static void ApplyControl(Control c)
    {
        c.ForeColor = Fore;

        switch (c)
        {
            case SplitContainer:
            case TableLayoutPanel:
            case GroupBox:
            case UserControl:
                c.BackColor = Back;
                break;

            case TextBox tb:
                tb.BackColor = Input;
                tb.ForeColor = Fore;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox cb:
                cb.BackColor = Input;
                cb.ForeColor = Fore;
                cb.FlatStyle = FlatStyle.Flat;
                break;

            case Button b:
                b.BackColor = Button;
                b.ForeColor = Fore;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 58);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(65, 65, 68);
                break;

            case Label:
                c.BackColor = Color.Transparent;
                break;

            case DataGridView grid:
                StyleGrid(grid);
                break;

            case MenuStrip menu:
                menu.BackColor = PanelColor;
                menu.ForeColor = Fore;
                break;

            case ProgressBar:
                c.BackColor = Panel2Color;
                break;

            default:
                c.BackColor = Back;
                break;
        }

        foreach (Control child in c.Controls)
            ApplyControl(child);
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Grid;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Panel2Color;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Fore;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Panel2Color;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Fore;
        grid.DefaultCellStyle.BackColor = Grid;
        grid.DefaultCellStyle.ForeColor = Fore;
        grid.DefaultCellStyle.SelectionBackColor = Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Fore;
        grid.RowHeadersDefaultCellStyle.BackColor = Panel2Color;
        grid.RowHeadersDefaultCellStyle.ForeColor = Fore;
    }
}

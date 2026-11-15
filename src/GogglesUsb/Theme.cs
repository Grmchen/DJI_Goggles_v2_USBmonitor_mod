using System;
using System.Drawing;
using System.Windows.Forms;

namespace GogglesUsb;

internal static class Theme
{
    public static readonly Color Bg = Color.White;
    public static readonly Color Field = Color.FromArgb(246, 247, 249);
    public static readonly Color Border = Color.FromArgb(214, 218, 224);
    public static readonly Color Text = Color.FromArgb(28, 32, 38);
    public static readonly Color Muted = Color.FromArgb(110, 118, 130);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentDown = Color.FromArgb(30, 64, 175);
    public static readonly Color Ok = Color.FromArgb(22, 163, 74);
    public static readonly Color Warn = Color.FromArgb(217, 119, 6);
    public static readonly Color Err = Color.FromArgb(220, 38, 38);

    public const string FontName = "Segoe UI";

    public static Font F(float size, bool bold = false) =>
        new(FontName, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
}

internal sealed class PillButton : Button
{
    public Color Fill = Theme.Field;
    public Color FillHover = Color.FromArgb(238, 241, 245);
    public Color FillDown = Color.FromArgb(228, 232, 238);
    public Color TextColor = Theme.Text;
    private bool _hover, _down;

    public PillButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Font = Theme.F(10f, true);
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var fill = !Enabled ? Color.FromArgb(240, 242, 245) : _down ? FillDown : _hover ? FillHover : Fill;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(fill))
        {
            g.FillRectangle(b, rect);
        }
        using (var pen = new Pen(Theme.Border))
        {
            g.DrawRectangle(pen, rect);
        }
        var color = !Enabled ? Color.FromArgb(160, 166, 176) : TextColor;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal sealed class StatusDot : Control
{
    public Color DotColor { get; set; } = Theme.Muted;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.Bg;
        Size = new Size(12, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var b = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(b, 2, 2, Width - 5, Height - 5);
    }
}

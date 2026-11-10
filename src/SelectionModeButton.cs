using System.Drawing.Drawing2D;
using SpaceUI;

namespace OotSceneViewer;

sealed class SelectionModeButton : SpaceButton
{
    private readonly bool zones;
    private bool active;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Active
    {
        get => active;
        set
        {
            active = value;
            BackColor = value ? Color.FromArgb(41, 75, 111) : SpaceTheme.Surface;
            FlatAppearance.BorderColor = value ? SpaceTheme.Accent : SpaceTheme.Border;
            AccessibleDescription = value ? "Current selection mode" : "Switch selection mode";
            Invalidate();
        }
    }

    internal SelectionModeButton(bool zones)
    {
        this.zones = zones;
        ButtonStyle = SpaceButtonStyle.Secondary;
        Size = new(34, 28);
        Margin = new(2, 3, 2, 3);
        AccessibleName = zones ? "Select zones" : "Select objects";
        Text = "";
        Active = !zones;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var saved = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TranslateTransform(Width / 2f, Height / 2f);
        float scale = DeviceDpi / 96f;
        e.Graphics.ScaleTransform(scale, scale);
        using var pen = new Pen(Enabled ? SpaceTheme.Text : SpaceTheme.Muted, 1.3f);
        if (zones)
        {
            pen.DashStyle = DashStyle.Dash;
            e.Graphics.DrawRectangle(pen, -8, -8, 16, 16);
            pen.DashStyle = DashStyle.Solid;
            e.Graphics.DrawLine(pen, -4, 0, 4, 0);
            e.Graphics.DrawLine(pen, 0, -4, 0, 4);
        }
        else
        {
            PointF[] outline = [new(0, -9), new(8, -4), new(8, 5), new(0, 9), new(-8, 5), new(-8, -4)];
            using var fill = new SolidBrush(Color.FromArgb(70, SpaceTheme.Accent));
            e.Graphics.FillPolygon(fill, outline);
            e.Graphics.DrawPolygon(pen, outline);
            e.Graphics.DrawLines(pen, [new PointF(-8, -4), new(0, 1), new(8, -4)]);
            e.Graphics.DrawLine(pen, 0, 1, 0, 9);
        }
        e.Graphics.Restore(saved);
    }
}

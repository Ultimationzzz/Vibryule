using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SpaceUI;

/// <summary>Small line chart. Input is copied, geometry is cached until data or size changes.</summary>
public class SpaceSparkline : Control
{
    private float[] values = [];
    private PointF[] points = [];
    public SpaceSparkline()
    {
        TabStop = false; Size = new(140, 28); ForeColor = SpaceTheme.Accent; BackColor = SpaceTheme.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    public void SetValues(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples) if (!float.IsFinite(sample)) throw new ArgumentException("Samples must be finite.", nameof(samples));
        if (samples.SequenceEqual(values)) return;
        values = samples.ToArray(); Rebuild();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Rebuild(); }
    private void Rebuild()
    {
        if (values.Length < 2) { points = []; Invalidate(); return; }
        float min = values.Min(), max = values.Max();
        points = new PointF[values.Length];
        for (int i = 0; i < values.Length; i++)
            points[i] = new PointF(2 + i * Math.Max(0, Width - 4f) / (values.Length - 1),
                max == min ? Height / 2f : 2 + (float)(((double)max - values[i]) / ((double)max - min)) * Math.Max(0, Height - 4));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (points.Length >= 2)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Enabled ? ForeColor : SpaceTheme.Disabled, DeviceDpi / 96f * 1.5f);
            e.Graphics.DrawLines(pen, points);
        }
        base.OnPaint(e);
    }
}

public class SpaceStatusRing : Control
{
    private int value;
    [DefaultValue(0)]
    public int Value
    {
        get => value;
        set { if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value)); if (this.value == value) return; this.value = value; Invalidate(); }
    }
    public SpaceStatusRing()
    {
        TabStop = false; Size = new(120, 120); ForeColor = SpaceTheme.Secondary; BackColor = SpaceTheme.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        int diameter = Math.Min(Width, Height) - 16;
        if (diameter > 0)
        {
            var bounds = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float thickness = Math.Max(2, diameter * .075f);
            using var track = new Pen(SpaceTheme.Elevated, thickness);
            using var fill = new Pen(Enabled ? ForeColor : SpaceTheme.Disabled, thickness);
            e.Graphics.DrawEllipse(track, bounds);
            if (value > 0) e.Graphics.DrawArc(fill, bounds, -90, value * 3.6f);
            TextRenderer.DrawText(e.Graphics, $"{value}%", Font, ClientRectangle, SpaceTheme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        base.OnPaint(e);
    }
}

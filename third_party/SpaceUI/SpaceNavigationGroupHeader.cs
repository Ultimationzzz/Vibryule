namespace SpaceUI;

/// <summary>A separated navigation group heading using the shared accent palette.</summary>
public class SpaceNavigationGroupHeader : SpaceLabel
{
    public SpaceNavigationGroupHeader()
    {
        AutoSize = false;
        Tone = SpaceTone.Accent;
        TextAlign = ContentAlignment.MiddleLeft;
        UseMnemonic = false;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(SpaceTheme.Border);
        int inset = LogicalToDeviceUnits(16),
            top = LogicalToDeviceUnits(6);
        if (Width > inset * 2)
            e.Graphics.DrawLine(pen, inset, top, Width - inset, top);
    }
}

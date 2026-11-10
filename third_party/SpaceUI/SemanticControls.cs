using System.ComponentModel;
using SpaceUI;

namespace SpaceUI;

public class SpaceSurfacePanel : Panel, ISpaceThemeAware
{
    private SpaceSurfaceKind kind;

    [DefaultValue(SpaceSurfaceKind.Surface)]
    public SpaceSurfaceKind Kind
    {
        get => kind;
        set
        {
            if (kind == value)
                return;
            kind = value;
            ApplyTheme(SpacePalette.Default);
        }
    }

    public SpaceSurfacePanel() => ApplyTheme(SpacePalette.Default);

    public void ApplyTheme(SpacePalette palette)
    {
        BackColor = SpaceStyle.Surface(Kind);
        ForeColor = palette.Text;
    }
}

public class SpaceFlowPanel : FlowLayoutPanel, ISpaceThemeAware
{
    private SpaceSurfaceKind kind;

    [DefaultValue(SpaceSurfaceKind.Surface)]
    public SpaceSurfaceKind Kind
    {
        get => kind;
        set
        {
            if (kind == value)
                return;
            kind = value;
            ApplyTheme(SpacePalette.Default);
        }
    }

    public SpaceFlowPanel() => ApplyTheme(SpacePalette.Default);

    public void ApplyTheme(SpacePalette palette)
    {
        BackColor = SpaceStyle.Surface(Kind);
        ForeColor = palette.Text;
    }
}

public class SpaceTableLayoutPanel : TableLayoutPanel, ISpaceThemeAware
{
    private SpaceSurfaceKind kind;

    [DefaultValue(SpaceSurfaceKind.Surface)]
    public SpaceSurfaceKind Kind
    {
        get => kind;
        set
        {
            if (kind == value)
                return;
            kind = value;
            ApplyTheme(SpacePalette.Default);
        }
    }

    public SpaceTableLayoutPanel() => ApplyTheme(SpacePalette.Default);

    public void ApplyTheme(SpacePalette palette)
    {
        BackColor = SpaceStyle.Surface(Kind);
        ForeColor = palette.Text;
    }
}

public class SpaceLabel : Label, ISpaceThemeAware
{
    private SpaceTone tone;

    [DefaultValue(SpaceTone.Default)]
    public SpaceTone Tone
    {
        get => tone;
        set
        {
            if (tone == value)
                return;
            tone = value;
            ApplyTheme(SpacePalette.Default);
        }
    }

    public SpaceLabel()
    {
        AutoSize = true;
        BackColor = Color.Transparent;
        ForeColor = SpaceStyle.Color(tone);
    }

    public void ApplyTheme(SpacePalette palette) => ForeColor = SpaceStyle.Color(Tone);
}

public enum SpaceButtonVariant
{
    Standard,
    Primary,
    Quiet,
    Danger,
}

/// <summary>SpaceUI's native button with toolkit glyphs and semantic action variants.</summary>
public class SpaceActionButton : SpaceButton, ISpaceThemeAware
{
    private SpaceButtonVariant variant;
    private SpaceGlyph glyph;
    private int glyphSize = 16;
    private (SpaceGlyph Glyph, int Pixels, Color Color) imageKey;
    private Bitmap? glyphImage;

    [DefaultValue(SpaceButtonVariant.Standard)]
    public SpaceButtonVariant Variant
    {
        get => variant;
        set
        {
            if (variant == value)
                return;
            variant = value;
            ApplyTheme(SpacePalette.Default);
        }
    }

    [DefaultValue(SpaceGlyph.None)]
    public SpaceGlyph Glyph
    {
        get => glyph;
        set
        {
            if (glyph == value)
                return;
            glyph = value;
            UpdateGlyph();
        }
    }

    [DefaultValue(16)]
    public int GlyphSize
    {
        get => glyphSize;
        set
        {
            if (glyphSize == value)
                return;
            glyphSize = Math.Clamp(value, 8, 48);
            UpdateGlyph();
        }
    }

    public SpaceActionButton()
    {
        TextImageRelation = TextImageRelation.ImageBeforeText;
        ApplyTheme(SpacePalette.Default);
    }

    public void ApplyTheme(SpacePalette palette)
    {
        ButtonStyle = Variant switch
        {
            SpaceButtonVariant.Primary => SpaceButtonStyle.Primary,
            SpaceButtonVariant.Danger => SpaceButtonStyle.Danger,
            _ => SpaceButtonStyle.Secondary,
        };
        BackColor = Variant switch
        {
            SpaceButtonVariant.Primary => palette.Accent,
            SpaceButtonVariant.Danger => palette.Danger,
            _ => palette.Surface,
        };
        ForeColor = Variant is SpaceButtonVariant.Primary or SpaceButtonVariant.Danger
            ? palette.OnAccent
            : palette.Text;
        FlatAppearance.BorderSize = Variant == SpaceButtonVariant.Quiet ? 0 : 1;
        FlatAppearance.BorderColor = palette.Border;
        FlatAppearance.MouseOverBackColor = SpacePalette.Mix(BackColor, palette.Text, .12);
        FlatAppearance.MouseDownBackColor = SpacePalette.Mix(BackColor, palette.Canvas, .25);
        UpdateGlyph();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateGlyph();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        UpdateGlyph();
    }

    private void UpdateGlyph()
    {
        var key = (Glyph, Math.Max(1, GlyphSize * DeviceDpi / 96), ForeColor);
        if (key == imageKey)
            return;
        imageKey = key;
        var previous = glyphImage;
        glyphImage = null;
        if (Glyph != SpaceGlyph.None)
        {
            glyphImage = new Bitmap(key.Item2, key.Item2);
            using var graphics = Graphics.FromImage(glyphImage);
            SpaceGlyphs.Draw(
                graphics,
                Glyph,
                new RectangleF(0, 0, key.Item2, key.Item2),
                ForeColor
            );
        }
        Image = glyphImage;
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Image = null;
            glyphImage?.Dispose();
            glyphImage = null;
        }
        base.Dispose(disposing);
    }
}

public class SpaceSearchBox : SpaceTextBox
{
    public event EventHandler? SearchRequested;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SearchRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
}

public class SpaceToolStripLabel : ToolStripLabel
{
    private SpaceTone tone;

    [DefaultValue(SpaceTone.Default)]
    public SpaceTone Tone
    {
        get => tone;
        set
        {
            if (tone == value)
                return;
            tone = value;
            ForeColor = SpaceStyle.Color(value);
        }
    }

    public SpaceToolStripLabel() => ForeColor = SpaceStyle.Color(tone);

    public SpaceToolStripLabel(string text)
        : this() => Text = text;
}

public class SpaceSemanticToolStripRenderer : SpaceToolStripRenderer
{
    public SpaceSemanticToolStripRenderer() { }

    public SpaceSemanticToolStripRenderer(SpacePalette palette) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is SpaceToolStripLabel label)
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                label.Enabled ? SpaceStyle.Color(label.Tone) : SpaceTheme.Disabled,
                e.TextFormat
            );
        else
            base.OnRenderItemText(e);
    }
}

public class SpaceSemanticToolStrip : SpaceToolStrip, ISpaceThemeAware
{
    public SpaceSemanticToolStrip() => ApplyTheme(SpacePalette.Default);

    public void ApplyTheme(SpacePalette palette)
    {
        if (Renderer is not SpaceSemanticToolStripRenderer)
            Renderer = new SpaceSemanticToolStripRenderer();
        BackColor = palette.Surface;
        ForeColor = palette.Text;
    }
}

public class SpaceSeparator : Control
{
    public SpaceSeparator()
    {
        Height = 1;
        TabStop = false;
        BackColor = SpaceTheme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(SpaceTheme.Border);
        e.Graphics.DrawLine(pen, 0, Height / 2, Width, Height / 2);
        base.OnPaint(e);
    }
}

public class SpaceSlider : TrackBar
{
    public SpaceSlider()
    {
        TickStyle = TickStyle.None;
        AutoSize = false;
        BackColor = SpaceTheme.Surface;
    }
}

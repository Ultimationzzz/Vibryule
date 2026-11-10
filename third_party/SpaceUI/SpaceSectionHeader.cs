using System.ComponentModel;

namespace SpaceUI;

/// <summary>A keyboard-operable section heading; the host owns the section's rows.</summary>
public class SpaceSectionHeader : UserControl, ISpaceThemeAware
{
    private readonly SpaceActionButton title = new()
    {
        Dock = DockStyle.Fill,
        Variant = SpaceButtonVariant.Quiet,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly SpaceActionButton reset = new()
    {
        Dock = DockStyle.Right,
        Text = "Reset section",
        Variant = SpaceButtonVariant.Quiet,
        Visible = false,
    };
    private bool expanded = true;
    private bool showReset;
    public event EventHandler? ExpandedChanged;
    public event EventHandler? ResetRequested;

    public SpaceSectionHeader()
    {
        Height = 34;
        TabStop = false;
        Controls.Add(title);
        Controls.Add(reset);
        title.Click += (_, _) => Expanded = !Expanded;
        reset.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        UpdateTitle();
    }

    [DefaultValue(true)]
    public bool Expanded
    {
        get => expanded;
        set
        {
            if (expanded == value)
                return;
            expanded = value;
            UpdateTitle();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public bool ShowReset
    {
        get => showReset;
        set
        {
            if (showReset == value)
                return;
            showReset = value;
            reset.Visible = value;
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (title != null)
            UpdateTitle();
    }

    private void UpdateTitle()
    {
        title.Text = (Expanded ? "\u2212  " : "+  ") + Text;
        title.AccessibleName = Text;
        title.AccessibleDescription = Expanded
            ? "Expanded. Activate to collapse."
            : "Collapsed. Activate to expand.";
        reset.AccessibleName = "Reset " + Text;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (reset != null)
            reset.Width = TextRenderer.MeasureText(reset.Text, Font).Width + 22 * DeviceDpi / 96;
        base.OnLayout(e);
    }

    public void ApplyTheme(SpacePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        title.ApplyTheme(palette);
        reset.ApplyTheme(palette);
    }
}

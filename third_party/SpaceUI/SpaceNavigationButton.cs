using System.ComponentModel;

namespace SpaceUI;

/// <summary>Native sidebar button with an optional hierarchy guide and selected accent.</summary>
public class SpaceNavigationButton : Button
{
    private bool selected;
    private int hierarchyLevel,
        continuingLevels;
    private bool lastSibling;

    [DefaultValue(false)]
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;
            selected = value;
            ApplySelection();
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HierarchyLevel => hierarchyLevel;

    public SpaceNavigationButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        TextAlign = ContentAlignment.MiddleLeft;
        AutoEllipsis = true;
        Padding = new Padding(16, 0, 8, 0);
        Margin = Padding.Empty;
        Height = 36;
        Cursor = Cursors.Hand;
        ApplySelection();
    }

    internal void SetHierarchy(int level, int ancestors, bool isLastSibling)
    {
        if (
            hierarchyLevel == level
            && continuingLevels == ancestors
            && lastSibling == isLastSibling
        )
            return;
        hierarchyLevel = level;
        continuingLevels = ancestors;
        lastSibling = isLastSibling;
        ApplySelection();
        Invalidate();
    }

    private void ApplySelection()
    {
        BackColor = selected ? Color.FromArgb(41, 75, 111) : SpaceTheme.Surface;
        ForeColor = selected || hierarchyLevel == 0 ? SpaceTheme.Text : SpaceTheme.Muted;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 76, 112);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(47, 85, 124);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Keep native text, hover, pressed, disabled, mnemonic and focus rendering.
        base.OnPaint(e);
        if (hierarchyLevel > 0)
        {
            using var guide = new Pen(selected ? SpaceTheme.Muted : SpaceTheme.Border);
            for (int level = 1; level < hierarchyLevel; level++)
                if ((continuingLevels & (1 << level)) != 0)
                {
                    int x = LogicalToDeviceUnits(24 + (level - 1) * 20);
                    e.Graphics.DrawLine(guide, x, 0, x, Height);
                }
            int branchX = LogicalToDeviceUnits(24 + (hierarchyLevel - 1) * 20);
            int middle = Height / 2;
            e.Graphics.DrawLine(guide, branchX, 0, branchX, lastSibling ? middle : Height);
            e.Graphics.DrawLine(
                guide,
                branchX,
                middle,
                LogicalToDeviceUnits(30 + (hierarchyLevel - 1) * 20),
                middle
            );
        }
        if (selected)
        {
            using var accent = new SolidBrush(Enabled ? SpaceTheme.Accent : SpaceTheme.Disabled);
            int inset = LogicalToDeviceUnits(7);
            e.Graphics.FillRectangle(
                accent,
                0,
                inset,
                LogicalToDeviceUnits(3),
                Math.Max(0, Height - inset * 2)
            );
        }
    }
}

using System.ComponentModel;
using System.Globalization;

namespace SpaceUI;

/// <summary>A live value editor with a precise numeric input, a drag track, units and reset.</summary>
[DefaultEvent(nameof(ValueChanged))]
public class SpaceValueEditor : UserControl, ISpaceThemeAware
{
    private readonly SpaceSliderTrack track = new() { Name = "valueTrack", TabIndex = 0 };
    private readonly SpaceNumericUpDown number = new()
    {
        Name = "valueNumber",
        TabIndex = 1,
        DecimalPlaces = 2,
        Increment = .01m,
    };
    private readonly SpaceLabel unit = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false,
    };
    private readonly SpaceActionButton reset = new()
    {
        Name = "resetValue",
        Text = "Reset",
        Variant = SpaceButtonVariant.Quiet,
        TabIndex = 2,
    };
    private bool updating;
    private decimal current;

    public SpaceValueEditor()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new(96, 96);
        Size = new(320, 32);
        TabStop = false;
        Controls.AddRange([track, number, unit, reset]);
        track.ValueChanged += (_, _) =>
        {
            if (!updating)
                Value = Minimum + (Maximum - Minimum) * track.Value / 1000m;
        };
        number.ValueChanged += (_, _) =>
        {
            if (!updating)
                Value = number.Value;
        };
        track.KeyDown += (_, e) =>
        {
            decimal? next = e.KeyCode switch
            {
                Keys.Left or Keys.Down => Value - Increment,
                Keys.Right or Keys.Up => Value + Increment,
                Keys.PageDown => Value - Increment * 10,
                Keys.PageUp => Value + Increment * 10,
                Keys.Home => Minimum,
                Keys.End => Maximum,
                _ => null,
            };
            if (next is { } value)
            {
                Value = value;
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        reset.Click += (_, _) => ResetToDefault();
        ApplyTheme(SpacePalette.Default);
    }

    public event EventHandler? ValueChanged;

    [DefaultValue(typeof(decimal), "0")]
    public decimal Value
    {
        get => current;
        set
        {
            decimal next = Math.Clamp(Math.Round(value, DecimalPlaces), Minimum, Maximum);
            bool changed = current != next;
            current = next;
            updating = true;
            try
            {
                if (number.Value != next)
                    number.Value = next;
                track.Value =
                    Maximum == Minimum
                        ? 0
                        : (int)Math.Round((next - Minimum) / (Maximum - Minimum) * 1000m);
                reset.Enabled =
                    Enabled
                    && next != Math.Clamp(Math.Round(ResetValue, DecimalPlaces), Minimum, Maximum);
            }
            finally
            {
                updating = false;
            }
            if (changed)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DefaultValue(typeof(decimal), "0")]
    public decimal Minimum
    {
        get => number.Minimum;
        set
        {
            if (number.Minimum == value)
                return;
            updating = true;
            try
            {
                number.Minimum = value;
            }
            finally
            {
                updating = false;
            }
            Value = current;
            PerformLayout();
        }
    }

    [DefaultValue(typeof(decimal), "100")]
    public decimal Maximum
    {
        get => number.Maximum;
        set
        {
            if (number.Maximum == value)
                return;
            updating = true;
            try
            {
                number.Maximum = value;
            }
            finally
            {
                updating = false;
            }
            Value = current;
            PerformLayout();
        }
    }

    [DefaultValue(2)]
    public int DecimalPlaces
    {
        get => number.DecimalPlaces;
        set
        {
            if (number.DecimalPlaces == value)
                return;
            number.DecimalPlaces = value;
            Value = current;
            PerformLayout();
        }
    }

    [DefaultValue(typeof(decimal), "0.01")]
    public decimal Increment
    {
        get => number.Increment;
        set => number.Increment = value;
    }

    private decimal resetValue;

    [DefaultValue(typeof(decimal), "0")]
    public decimal ResetValue
    {
        get => resetValue;
        set
        {
            if (resetValue == value)
                return;
            resetValue = value;
            Value = current;
        }
    }

    [DefaultValue("")]
    public string Units
    {
        get => unit.Text;
        set
        {
            if (unit.Text == value)
                return;
            unit.Text = value;
            PerformLayout();
        }
    }

    [Browsable(false)]
    public virtual bool IsEditing => ContainsFocus || track.Capture || number.ContainsFocus;

    /// <summary>Refresh a published value without disrupting an in-progress edit.</summary>
    public void SetSnapshotValue(decimal value)
    {
        if (!IsEditing)
            Value = value;
    }

    public void ResetToDefault() => Value = ResetValue;

    public void ApplyTheme(SpacePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.Text;
        number.BackColor = palette.Canvas;
        number.ForeColor = palette.Text;
        track.ApplyTheme(palette);
        unit.ApplyTheme(palette);
        reset.ApplyTheme(palette);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (reset != null)
            Value = current;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (number == null)
            return;
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        int gap = Px(6),
            height = ClientSize.Height;
        int resetWidth = TextRenderer.MeasureText(reset.Text, Font).Width + Px(16);
        int unitWidth = string.IsNullOrEmpty(Units)
            ? 0
            : TextRenderer.MeasureText(Units, Font).Width + gap;
        string format = "F" + DecimalPlaces;
        int numberWidth = Math.Max(
            Px(78),
            Math.Max(
                TextRenderer
                    .MeasureText(Minimum.ToString(format, CultureInfo.CurrentCulture), Font)
                    .Width,
                TextRenderer
                    .MeasureText(Maximum.ToString(format, CultureInfo.CurrentCulture), Font)
                    .Width
            ) + Px(28)
        );
        int trackWidth = ClientSize.Width - resetWidth - unitWidth - numberWidth - gap * 2;
        bool showTrack = trackWidth >= Px(54);
        track.Visible = showTrack;
        if (showTrack)
            track.SetBounds(0, 0, trackWidth, height);
        int x = showTrack ? trackWidth + gap : 0;
        number.SetBounds(
            x,
            Math.Max(0, (height - number.PreferredHeight) / 2),
            Math.Max(1, ClientSize.Width - x - unitWidth - resetWidth - gap),
            number.PreferredHeight
        );
        unit.SetBounds(number.Right, 0, unitWidth, height);
        reset.SetBounds(unit.Right + gap, 0, resetWidth, height);
        string name = AccessibleName ?? Name;
        track.AccessibleName = name;
        number.AccessibleName = name + (Units.Length > 0 ? " (" + Units + ")" : "");
        reset.AccessibleName = "Reset " + name;
    }
}

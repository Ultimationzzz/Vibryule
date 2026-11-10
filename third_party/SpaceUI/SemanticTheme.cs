using System.Runtime.CompilerServices;
using SpaceUI;

namespace SpaceUI;

/// <summary>Semantic colors for toolkit-owned paint surfaces, derived from the installed SpaceUI palette.</summary>
public sealed class SpacePalette
{
    public static SpacePalette Default { get; } = new();
    public Color Canvas => SpaceTheme.Background;
    public Color Surface => SpaceTheme.Surface;
    public Color SurfaceHover => SpaceTheme.Elevated;
    public Color ScrollThumb => SpaceTheme.Muted;
    public Color ScrollThumbHot => SpaceTheme.Secondary;
    public Color Border => SpaceTheme.Border;
    public Color BorderStrong => SpaceTheme.Muted;
    public Color Text => SpaceTheme.Text;
    public Color Muted => SpaceTheme.Muted;
    public Color Accent => SpaceTheme.Accent;
    public Color AccentHover => SpaceTheme.Secondary;
    public Color Selection { get; } = Mix(SpaceTheme.Surface, SpaceTheme.Primary, .25);
    public Color FocusRing => SpaceTheme.Primary;
    public Color OnAccent => SpaceTheme.Background;
    public Color ControlDisabled => SpaceTheme.Elevated;
    public Color TextDisabled => SpaceTheme.Disabled;
    public Color Danger => SpaceTheme.Danger;
    public Color Warning => SpaceTheme.Warning;
    public Color Success => SpaceTheme.Success;

    public static Color Mix(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(first.A + (second.A - first.A) * amount),
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount)
        );
    }
}

public enum SpaceTone
{
    Default,
    Muted,
    Accent,
    Success,
    Warning,
    Danger,
}

public enum SpaceSurfaceKind
{
    Surface,
    Canvas,
}

public interface ISpaceThemeAware
{
    void ApplyTheme(SpacePalette palette);
}

/// <summary>Styles custom surfaces at insertion, never from a poll or paint callback.</summary>
public static class SpaceStyle
{
    private sealed class Registration;

    private static readonly ConditionalWeakTable<Control, Registration> registered = new();

    public static SpacePalette GetPalette(Control control) => SpacePalette.Default;

    public static Color Color(SpaceTone tone) =>
        tone switch
        {
            SpaceTone.Muted => SpaceTheme.Muted,
            SpaceTone.Accent => SpaceTheme.Accent,
            SpaceTone.Success => SpaceTheme.Success,
            SpaceTone.Warning => SpaceTheme.Warning,
            SpaceTone.Danger => SpaceTheme.Danger,
            _ => SpaceTheme.Text,
        };

    public static Color Surface(SpaceSurfaceKind kind) =>
        kind == SpaceSurfaceKind.Canvas ? SpaceTheme.Background : SpaceTheme.Surface;

    public static void Register(Control control)
    {
        if (control is ISpaceThemeAware aware)
            aware.ApplyTheme(SpacePalette.Default);
        if (!registered.TryGetValue(control, out _))
        {
            registered.Add(control, new());
            control.ControlAdded += ChildAdded;
        }
        foreach (Control child in control.Controls)
            Register(child);
    }

    private static void ChildAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null)
            Register(e.Control);
    }
}

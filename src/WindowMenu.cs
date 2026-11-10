using SpaceUI;

namespace OotSceneViewer;

public partial class Form1
{
    private readonly ToolStripMenuItem openNupMenu = new("&Open NUP…") { ShortcutKeys = Keys.Control | Keys.O };
    private readonly ToolStripMenuItem openPackageMenu = new("Open &Prepared Scene…") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.O };
    private readonly ToolStripMenuItem changeSceneMenu = new("&Change Scene…") { ShortcutKeys = Keys.Control | Keys.L };
    private readonly ToolStripMenuItem loadedNupMenu = new("No NUP loaded") { Enabled = false };
    private readonly ToolStripMenuItem frameSceneMenu = new("&Frame Scene") { ShortcutKeys = Keys.Control | Keys.Home };
    private readonly ToolStripMenuItem focusObjectMenu = new("Focus &Selected Object") { ShortcutKeys = Keys.Control | Keys.F };
    private readonly ToolStripMenuItem topViewMenu = new("&Top View") { ShortcutKeys = Keys.Control | Keys.T };
    private readonly ToolStripMenuItem selectZonesMenu = new("Select &Zones Only") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.Z };

    private MenuStrip BuildWindowMenu()
    {
        var menu = new SpaceMenuStrip { Dock = DockStyle.Fill, AutoSize = false, ShowItemToolTips = true };
        var file = new ToolStripMenuItem("&File");
        var scene = new ToolStripMenuItem("&Scene");
        var display = new ToolStripMenuItem("&View");
        var help = new ToolStripMenuItem("&Help");
        menu.Items.AddRange([file, scene, display, help]);
        openNupMenu.Click += async (_, _) => await OpenNupAsync();
        openPackageMenu.Click += (_, _) => Open();
        changeSceneMenu.Click += async (_, _) => await ChangeSceneAsync();
        frameSceneMenu.Click += (_, _) => view.FrameScene();
        focusObjectMenu.Click += (_, _) => { if (view.Selected != null) view.FocusActor(view.Selected); };
        topViewMenu.Click += (_, _) => view.TopView();
        file.DropDownItems.AddRange([openNupMenu, openPackageMenu, new ToolStripSeparator(), new ToolStripMenuItem("E&xit", null, (_, _) => Close())]);
        scene.DropDownItems.AddRange([changeSceneMenu, loadedNupMenu, new ToolStripSeparator(), frameSceneMenu, focusObjectMenu, topViewMenu]);
        display.DropDownItems.Add(new ToolStripMenuItem("&Selection Details", null, (_, _) => sidebarTabs.SelectedIndex = 0) { ShortcutKeys = Keys.Control | Keys.D1 });
        display.DropDownItems.Add(new ToolStripMenuItem("&Display && Filters", null, (_, _) => sidebarTabs.SelectedIndex = 1) { ShortcutKeys = Keys.Control | Keys.D2 });
        selectZonesMenu.Click += (_, _) => pickZones.Checked = !pickZones.Checked;
        display.DropDownItems.Add(selectZonesMenu);
        display.DropDownItems.Add(new ToolStripSeparator());
        display.DropDownItems.Add(new ToolStripMenuItem("&Reset Panel Widths", null, (_, _) =>
        {
            objectSplit.SplitterDistance = 300;
            inspectorSplit.SplitterDistance = Math.Max(inspectorSplit.Panel1MinSize, inspectorSplit.Width - inspectorSplit.SplitterWidth - 400);
        }));
        help.DropDownItems.Add(new ToolStripMenuItem("&Controls", null, (_, _) => MessageBox.Show(this,
            "WASD: fly\nR / F: up / down\nShift / Ctrl: faster / slower\nLeft or right drag: look\nMiddle drag: pan\nWheel or Z / X: speed\nHome: focus selection\nT: top view\n\nCtrl+L: change scene in the loaded NUP\nCtrl+1 / Ctrl+2: selection / display tabs\n\nDrag either map divider to resize its side panel.",
            "Vibryule controls", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        foreach (var item in new[] { file, scene, display })
            item.DropDownOpening += (_, _) => UpdateMenuState();
        return menu;
    }

    private void UpdateMenuState()
    {
        openNupMenu.Enabled = openPackageMenu.Enabled = !workflowBusy;
        changeSceneMenu.Enabled = !workflowBusy && loadedNupPath != null && availableScenes.Count > 0;
        frameSceneMenu.Enabled = topViewMenu.Enabled = !workflowBusy && actors.Count > 0;
        focusObjectMenu.Enabled = !workflowBusy && view.Selected != null;
        loadedNupMenu.Text = loadedNupPath == null ? "No NUP loaded" : $"{availableScenes.Count} scenes · {Path.GetFileName(loadedNupPath)}";
        changeSceneMenu.ToolTipText = loadedNupPath == null ? "Open a NUP first using File → Open NUP." : "Choose another scene from the loaded NUP.";
    }
}

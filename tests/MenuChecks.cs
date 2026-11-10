using SpaceUI;

namespace OotSceneViewer;

public partial class Form1
{
    internal void CheckWindowMenu()
    {
        if (MainMenuStrip is not SpaceMenuStrip || changeSceneMenu.Enabled)
            throw new InvalidOperationException("Empty startup must have a themed menu and no scene-switch command.");
        var first = new SceneOption("First", "First_Child", ["Static"]);
        var second = new SceneOption("Second", "Second_Adult", ["Static"]);
        try
        {
            loadedNupPath = Path.Combine(Path.GetTempPath(), "menu-check.nup");
            availableScenes = [first, second];
            currentNupScene = second;
            UpdateMenuState();
            if (!changeSceneMenu.Enabled || changeSceneMenu.ShortcutKeys != (Keys.Control | Keys.L))
                throw new InvalidOperationException("An indexed NUP did not enable scene switching.");
            using var picker = new ScenePicker(availableScenes, currentNupScene, "menu-check.nup");
            if (!ReferenceEquals(picker.Selection, second))
                throw new InvalidOperationException("Scene chooser did not preselect the current scene.");
            var search = picker.Controls.OfType<SpaceSearchBox>().Single();
            search.Text = "__no_match__";
            if (picker.Selection != null || ((Button)picker.AcceptButton!).Enabled)
                throw new InvalidOperationException("Empty scene search allowed an invalid selection.");
            search.Text = "First";
            if (!ReferenceEquals(picker.Selection, first))
                throw new InvalidOperationException("Scene search did not select the matching scene.");
            // Cancelling the chooser must not discard the indexed NUP or current scene.
            picker.DialogResult = DialogResult.Cancel;
            if (!ReferenceEquals(currentNupScene, second) || availableScenes.Count != 2)
                throw new InvalidOperationException("Scene chooser changed the active session before loading.");
            workflowBusy = true;
            UpdateMenuState();
            if (changeSceneMenu.Enabled || openNupMenu.Enabled || openPackageMenu.Enabled)
                throw new InvalidOperationException("Preparation allowed a competing open operation.");
        }
        finally
        {
            loadedNupPath = null;
            availableScenes = [];
            currentNupScene = null;
            workflowBusy = false;
            UpdateMenuState();
        }
    }
}

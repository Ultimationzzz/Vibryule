using SpaceUI;

namespace OotSceneViewer;

record SceneOption(string Folder, string Name, string[] Kinds)
{
    public override string ToString() => $"{Name}  ·  {Folder}";
}

sealed class NupWorkflow
{
    private readonly string cache;

    public NupWorkflow()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(local, "Vibryule", "cache");
        cache = Environment.GetEnvironmentVariable("VIBRYULE_CACHE") ?? current;
    }

    public async Task<List<SceneOption>> ListAsync(string nup, IProgress<string> progress, CancellationToken cancellation)
    {
        progress.Report("Reading NUP scene index…");
        return await Task.Run(() => ScenePreparation.List(nup, cancellation), cancellation);
    }

    public async Task<string> PrepareAsync(string nup, SceneOption option, IProgress<string> progress, CancellationToken cancellation)
    {
        return await Task.Run(() => ScenePreparation.Prepare(nup, option, cache, progress, cancellation), cancellation);
    }
}

sealed class ScenePicker : SpaceForm
{
    private readonly SpaceListBox list = new() { Dock = DockStyle.Fill };
    private readonly SpaceSearchBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search scenes" };
    private readonly List<SceneOption> scenes;
    public SceneOption? Selection => list.SelectedItem as SceneOption;

    public ScenePicker(List<SceneOption> scenes)
    {
        this.scenes = scenes;
        Text = "Choose a scene";
        Width = 580;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);
        var top = new Label { Text = "Choose a scene from your local NUP", Dock = DockStyle.Top, Height = 36, ForeColor = SpaceTheme.Text };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        var open = new SpaceButton { Text = "Prepare scene", Width = 130, DialogResult = DialogResult.OK };
        var cancel = new SpaceButton { Text = "Cancel", Width = 100, ButtonStyle = SpaceButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(open);
        actions.Controls.Add(cancel);
        AcceptButton = open;
        CancelButton = cancel;
        Controls.Add(new SpaceScrollHost(list));
        Controls.Add(search);
        Controls.Add(top);
        Controls.Add(actions);
        search.TextChanged += (_, _) => RefreshScenes();
        list.DoubleClick += (_, _) => { if (Selection != null) { DialogResult = DialogResult.OK; Close(); } };
        open.Click += (_, _) => { if (Selection == null) DialogResult = DialogResult.None; };
        RefreshScenes();
    }
    private void RefreshScenes()
    {
        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(scenes.Where(x => x.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || x.Folder.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
        list.EndUpdate();
    }


}

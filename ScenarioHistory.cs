using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace BreedersScenarioAnalyzer;

internal readonly record struct ScenarioHistoryKey(int SingleModeCharaId, int Turn);

internal sealed class ScenarioHistory : IDisposable
{
    const string PanelKey = "training";

    readonly Lock gate = new();
    readonly IApplication application;
    readonly Workspace workspace;
    readonly WorkspaceContent panelContent;
    readonly List<Entry> entries = [];
    readonly HashSet<HistoryView> views = [];

    WorkspaceContent? liveSnapshot;
    int limit;
    int selectedIndex = -1;
    bool unread;
    bool published;
    bool disposed;

    internal ScenarioHistory(IApplication application, Workspace workspace, int limit)
    {
        this.application = application;
        this.workspace = workspace;
        this.limit = limit;
        panelContent = new(() => new HistoryView(this, application, workspace));
    }

    internal void Publish(ScenarioHistoryKey key, WorkspaceContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        WorkspaceContent? display = null;
        HistoryView[] targets;
        var notifyUnread = false;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            workspace.SetPanel(
                PanelKey,
                "训练分析",
                panelContent,
                fullBleed: true,
                switchToWorkspace: !published);
            published = true;

            liveSnapshot = content;

            if (limit == 0)
            {
                entries.Clear();
                selectedIndex = -1;
                unread = false;
                display = content;
            }
            else
            {
                var existingIndex = entries.FindIndex(entry => entry.Key == key);
                if (existingIndex >= 0)
                {
                    entries[existingIndex] = new(key, content);
                    if (selectedIndex == existingIndex)
                        display = content;
                }
                else
                {
                    var followedNewest = selectedIndex < 0 || selectedIndex == entries.Count - 1;
                    entries.Add(new(key, content));
                    if (followedNewest)
                    {
                        selectedIndex = entries.Count - 1;
                        unread = false;
                        display = content;
                    }
                    else
                    {
                        notifyUnread = !unread;
                        unread = true;
                    }

                    var overflow = entries.Count - limit;
                    if (overflow > 0)
                    {
                        entries.RemoveRange(0, overflow);
                        selectedIndex -= overflow;
                        if (selectedIndex < 0)
                        {
                            selectedIndex = entries.Count - 1;
                            unread = false;
                            notifyUnread = false;
                            display = entries[selectedIndex].Content;
                        }
                    }
                }
            }

            targets = [.. views];
        }

        if (display is not null)
            UpdateViews(targets, display);
        if (notifyUnread)
            NotifyIfActive("有新的训练分析记录，按 → 查看最新。");
    }

    internal void SetLimit(int value)
    {
        WorkspaceContent? display = null;
        HistoryView[] targets;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            limit = value;
            if (limit == 0)
            {
                entries.Clear();
                selectedIndex = -1;
                unread = false;
                display = liveSnapshot;
            }
            else
            {
                var overflow = entries.Count - limit;
                if (overflow > 0)
                {
                    entries.RemoveRange(0, overflow);
                    selectedIndex -= overflow;
                    if (selectedIndex < 0)
                    {
                        selectedIndex = entries.Count - 1;
                        unread = false;
                        display = entries[selectedIndex].Content;
                    }
                }
            }
            targets = [.. views];
        }

        if (display is not null)
            UpdateViews(targets, display);
    }

    internal WorkspaceContent Attach(HistoryView view)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var content = SelectedContentLocked()
                ?? throw new InvalidOperationException("梦想杯 history panel 在首个输出前被创建。");
            views.Add(view);
            return content;
        }
    }

    internal void Detach(HistoryView view)
    {
        lock (gate)
            views.Remove(view);
    }

    internal bool Navigate(KeyCode keyCode, out WorkspaceContent? content, out string? position)
    {
        lock (gate)
        {
            content = null;
            position = null;
            if (disposed || limit == 0 || entries.Count == 0)
                return false;

            selectedIndex = keyCode switch
            {
                KeyCode.CursorUp => Math.Max(0, selectedIndex - 1),
                KeyCode.CursorDown => Math.Min(entries.Count - 1, selectedIndex + 1),
                KeyCode.CursorLeft => 0,
                KeyCode.CursorRight => entries.Count - 1,
                _ => selectedIndex
            };
            content = entries[selectedIndex].Content;
            if (selectedIndex == entries.Count - 1)
                unread = false;
            position = $"历史记录 {selectedIndex + 1}/{entries.Count}";
            return true;
        }
    }

    internal void NotifyIfActive(string message)
    {
        lock (gate)
        {
            if (!disposed)
                workspace.Notify(message);
        }
    }

    public void Dispose()
    {
        HistoryView[] targets;
        var removePanel = false;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            entries.Clear();
            liveSnapshot = null;
            selectedIndex = -1;
            unread = false;
            targets = [.. views];
            views.Clear();
            removePanel = published;
            published = false;
        }

        foreach (var view in targets)
            view.Deactivate();
        if (removePanel)
            workspace.RemovePanel(PanelKey);
    }

    WorkspaceContent? SelectedContentLocked()
        => limit > 0 && selectedIndex >= 0
            ? entries[selectedIndex].Content
            : liveSnapshot;

    void UpdateViews(HistoryView[] targets, WorkspaceContent content)
    {
        if (targets.Length == 0)
            return;

        void Update()
        {
            lock (gate)
            {
                if (disposed)
                    return;
            }
            foreach (var view in targets)
                view.Show(content);
        }

        if (Environment.CurrentManagedThreadId == application.MainThreadId)
            Update();
        else
            application.Invoke(Update);
    }

    sealed record Entry(ScenarioHistoryKey Key, WorkspaceContent Content);
}

internal sealed class HistoryView : View
{
    readonly ScenarioHistory owner;
    readonly IApplication application;
    readonly Workspace workspace;
    View? contentView;
    volatile bool active = true;

    internal HistoryView(ScenarioHistory owner, IApplication application, Workspace workspace)
    {
        this.owner = owner;
        this.application = application;
        this.workspace = workspace;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar;
        application.Keyboard.KeyDown += ApplicationKeyDown;
        try
        {
            Show(owner.Attach(this));
        }
        catch
        {
            Deactivate();
            owner.Detach(this);
            if (contentView is { } failedContent)
            {
                Remove(failedContent);
                failedContent.Dispose();
                contentView = null;
            }
            throw;
        }
        Initialized += (_, _) =>
        {
            if (ReferenceEquals(Workspace.Current, workspace))
                SetFocus();
        };
        MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
                ScrollContent(1);
            else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
                ScrollContent(-1);
            else
                return;
            mouse.Handled = true;
        };
    }

    internal void Show(WorkspaceContent content)
    {
        if (!active)
            return;

        var replacement = content.CreateView();
        replacement.X = 0;
        replacement.Y = 0;
        replacement.Width = Dim.Fill();
        var old = contentView;
        contentView = replacement;
        if (old is not null)
        {
            Remove(old);
            old.Dispose();
        }
        Add(replacement);
        Viewport = new(0, 0, Viewport.Width, Viewport.Height);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    internal void Deactivate()
    {
        if (!active)
            return;
        active = false;
        application.Keyboard.KeyDown -= ApplicationKeyDown;
    }

    protected override void OnSubViewLayout(LayoutEventArgs args)
    {
        base.OnSubViewLayout(args);
        var width = Math.Max(1, Viewport.Width);
        var height = Math.Max(1, Viewport.Height);
        var contentHeight = Math.Max(height, contentView?.Frame.Bottom ?? 0);
        SetContentSize(new Size(width, contentHeight));
        var maxY = Math.Max(0, contentHeight - height);
        if (Viewport.Y > maxY)
            Viewport = new(0, maxY, Viewport.Width, Viewport.Height);
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.PageDown)
            return ScrollContent(Math.Max(1, Viewport.Height - 1));
        if (key == Key.PageUp)
            return ScrollContent(-Math.Max(1, Viewport.Height - 1));
        if (key == Key.Home)
            return ScrollTo(0);
        if (key == Key.End)
            return ScrollTo(Math.Max(0, GetContentSize().Height - Viewport.Height));
        return base.OnKeyDown(key);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Deactivate();
            owner.Detach(this);
        }
        base.Dispose(disposing);
    }

    void ApplicationKeyDown(object? sender, Key key)
    {
        if (key.Handled || key.IsCtrl || key.IsAlt || key.IsShift || !active ||
            !ReferenceEquals(Workspace.Current, workspace) || !ContainsFocus())
        {
            return;
        }
        if (key.KeyCode is not (KeyCode.CursorUp or KeyCode.CursorDown or
            KeyCode.CursorLeft or KeyCode.CursorRight))
        {
            return;
        }
        if (!owner.Navigate(key.KeyCode, out var content, out var position))
            return;

        key.Handled = true;
        Show(content!);
        owner.NotifyIfActive(position!);
    }

    bool ContainsFocus()
    {
        for (var focused = application.TopRunnableView?.MostFocused;
             focused is not null;
             focused = focused.SuperView)
        {
            if (ReferenceEquals(focused, this))
                return true;
        }
        return false;
    }

    bool ScrollContent(int delta)
        => ScrollTo(Viewport.Y + delta);

    bool ScrollTo(int y)
    {
        var maxY = Math.Max(0, GetContentSize().Height - Viewport.Height);
        var next = Math.Clamp(y, 0, maxY);
        if (next == Viewport.Y)
            return true;
        Viewport = new(0, next, Viewport.Width, Viewport.Height);
        SetNeedsDraw();
        return true;
    }
}

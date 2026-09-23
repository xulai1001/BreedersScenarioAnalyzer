using Gallop;
using Gallop.Endpoints;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;

namespace BreedersScenarioAnalyzer;

public sealed class BreedersScenarioAnalyzer : IPlugin
{
    const string InternalName = "BreedersScenarioAnalyzer";

    readonly Lock lifecycleGate = new();
    IApplication? application;
    Workspace? workspace;
    Handler? handler;
    ScenarioHistory? history;
    int historyLimit = HistorySettings.DefaultLimit;
    long generation;
    bool accepting;

    string SettingsPath => Path.Combine("PluginData", InternalName, "settings.json");
    public void Initialize(IPluginContext context)
    {
        var loadedLimit = HistorySettings.Load(SettingsPath).HistoryLimit;
        lock (lifecycleGate)
        {
            generation++;
            application = context.Application;
            historyLimit = loadedLimit;
            handler = new();
            accepting = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        ScenarioHistory? retiringHistory;
        lock (lifecycleGate)
        {
            if (!accepting)
                return ValueTask.CompletedTask;
            accepting = false;
            retiringHistory = history;
            history = null;
            handler = null;
            workspace = null;
            application = null;
        }
        retiringHistory?.Dispose();
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeBreeders.CheckEvent>(1)]
    public ValueTask Analyze(SingleModeBreedersCheckEventResponse response)
    {
        var data = response.data;
        if (data.home_info.command_info_array is null || data.chara_info.state is 2 or 3)
            return ValueTask.CompletedTask;
        if ((data.unchecked_event_array is { Length: > 0 }) || data.race_start_info is not null)
            return ValueTask.CompletedTask;

        Handler analyzer;
        long activeGeneration;
        lock (lifecycleGate)
        {
            if (!accepting)
                return ValueTask.CompletedTask;
            analyzer = handler!;
            activeGeneration = generation;
        }

        var key = new ScenarioHistoryKey(data.chara_info.single_mode_chara_id, data.chara_info.turn);
        var content = analyzer.ParseBreederCommandInfo(response);
        lock (lifecycleGate)
        {
            if (!accepting || generation != activeGeneration)
                return ValueTask.CompletedTask;

            var target = workspace ?? Workspace.Create("BreedersScenarioAnalyzer");
            var targetHistory = history ?? new ScenarioHistory(application!, target, historyLimit);
            try
            {
                targetHistory.Publish(key, content);
            }
            catch
            {
                if (history is null)
                    targetHistory.Dispose();
                throw;
            }
            workspace ??= target;
            history ??= targetHistory;
        }
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeBreeders.Load>(1)]
    public ValueTask Analyze(SingleModeBreedersLoadResponse response)
    {
        lock (lifecycleGate)
        {
            if (accepting)
                handler!.Load(response);
        }
        return ValueTask.CompletedTask;
    }

    public async Task ConfigPromptAsync(
        IApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        cancellationToken.ThrowIfCancellationRequested();
        if (application.TopRunnable is null &&
            Environment.CurrentManagedThreadId != application.MainThreadId)
        {
            throw new InvalidOperationException(
                "梦想杯剧本解析器无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");
        }

        var draft = HistorySettings.Load(SettingsPath);
        HistorySettings saved;
        if (Environment.CurrentManagedThreadId == application.MainThreadId)
        {
            saved = RunConfigDialog(application, draft, cancellationToken);
        }
        else
        {
            var completion = new TaskCompletionSource<HistorySettings>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            application.Invoke(() =>
            {
                try
                {
                    completion.SetResult(RunConfigDialog(application, draft, cancellationToken));
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });
            saved = await completion.Task;
        }

        cancellationToken.ThrowIfCancellationRequested();
        saved.Save(SettingsPath);
        lock (lifecycleGate)
        {
            historyLimit = saved.HistoryLimit;
            if (accepting)
                history?.SetLimit(historyLimit);
        }
    }

    static HistorySettings RunConfigDialog(
        IApplication application,
        HistorySettings draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new Dialog
        {
            Title = "梦想杯剧本解析器配置",
            Width = 54,
            Height = 10
        };
        var historyLimit = new NumericUpDown<int>
        {
            X = 23,
            Y = 1,
            Width = 12,
            Value = draft.HistoryLimit,
            Increment = 1
        };
        var validation = new Label
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Height = 1
        };
        dialog.Add(
            new Label { X = 1, Y = 1, Text = "History 上限（0–1000）" },
            historyLimit,
            validation);

        var accepted = false;
        var save = new Button { Text = "保存", IsDefault = true };
        save.Accepting += (_, e) =>
        {
            if (!HistorySettings.IsValid(historyLimit.Value))
            {
                validation.Text = "History 上限必须在 0 到 1000 之间。";
                e.Handled = true;
                return;
            }

            accepted = true;
            application.RequestStop(dialog);
            e.Handled = true;
        };
        var cancel = new Button { Text = "取消" };
        cancel.Accepting += (_, e) =>
        {
            application.RequestStop(dialog);
            e.Handled = true;
        };
        dialog.AddButton(cancel);
        dialog.AddButton(save);
        historyLimit.SetFocus();

        using (cancellationToken.Register(
                   () => application.Invoke(() => application.RequestStop(dialog))))
        {
            application.Run(dialog);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!accepted)
            throw new OperationCanceledException("梦想杯剧本解析器配置已取消。", cancellationToken);

        return new() { HistoryLimit = historyLimit.Value };
    }

}

public static class BreedersExtensions
{
    extension(SingleModeBreedersTeamMemberInfo charaInfo)
    {
        public string Name => Database.Names.DisplayNickname(charaInfo.chara_id);

        public string Rank => TurnInfoBreeders.TeamMemberRank[charaInfo.rank - 1];

        public string Explain => $"{charaInfo.Name}{charaInfo.Rank}{charaInfo.exp}";
    }
}

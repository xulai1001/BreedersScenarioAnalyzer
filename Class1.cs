using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.TerminalGui;
using UmamusumeResponseAnalyzer.Plugin;

namespace BreedersScenarioAnalyzer;

public sealed class BreedersScenarioAnalyzer : IPlugin
{
    Workspace? workspace;
    Handler? handler;
    bool hasPublishedTrainingPanel;

    public void Initialize(IPluginContext context)
    {
        handler = new();
        hasPublishedTrainingPanel = false;
    }

    public void Dispose()
    {
        handler = null;
        if (!hasPublishedTrainingPanel)
            return;

        workspace!.RemovePanel("training");
        hasPublishedTrainingPanel = false;
    }

    [ResponseAnalyzer<GameApi.SingleModeBreeders.CheckEvent>(1)]
    public ValueTask Analyze(SingleModeBreedersCheckEventResponse response)
    {
        var data = response.data;
        if (data.home_info.command_info_array is null || data.chara_info.state is 2 or 3)
            return ValueTask.CompletedTask;
        if ((data.unchecked_event_array is { Length: > 0 }) || data.race_start_info is not null)
            return ValueTask.CompletedTask;

        var content = Analyzer.ParseBreederCommandInfo(response);
        var workspace = this.workspace ??= Workspace.Create("BreedersScenarioAnalyzer");
        workspace.SetPanel(
            "training",
            "训练分析",
            content,
            fullBleed: true,
            switchToWorkspace: !hasPublishedTrainingPanel);
        hasPublishedTrainingPanel = true;
        return ValueTask.CompletedTask;
    }

    [ResponseAnalyzer<GameApi.SingleModeBreeders.Load>(1)]
    public ValueTask Analyze(SingleModeBreedersLoadResponse response)
    {
        Analyzer.Load(response);
        return ValueTask.CompletedTask;
    }

    Handler Analyzer => handler
        ?? throw new InvalidOperationException("BreedersScenarioAnalyzer 尚未初始化 analyzer。");
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

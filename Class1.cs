using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;

namespace BreedersScenarioAnalyzer;

public sealed class BreedersScenarioAnalyzer : IPlugin
{
    ILiveDisplayOutput? liveDisplay;
    LiveDisplayWorkspace? workspace;
    Handler? handler;
    bool hasPublishedTrainingPanel;

    public string Name => "梦想杯剧本解析器";

    public string Author => "UmamusumeResponseAnalyzer";

    public string[] Targets => ["Cygames"];

    public void Initialize(IPluginContext context)
    {
        liveDisplay = context.LiveDisplay;
        handler = new();
        hasPublishedTrainingPanel = false;
    }

    public void Dispose()
    {
        if (liveDisplay is not null && workspace is not null)
            liveDisplay.RemoveWorkspace(workspace);

        liveDisplay = null;
        workspace = null;
        handler = null;
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
        LiveDisplay.SetPanel(
            Workspace,
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

    ILiveDisplayOutput LiveDisplay => liveDisplay
        ?? throw new InvalidOperationException("BreedersScenarioAnalyzer 尚未初始化 LiveDisplay。");

    LiveDisplayWorkspace Workspace => workspace
        ??= LiveDisplay.CreateWorkspace("BreedersScenarioAnalyzer");

    Handler Analyzer => handler
        ?? throw new InvalidOperationException("BreedersScenarioAnalyzer 尚未初始化 analyzer。");
}

public static class BreedersExtensions
{
    extension(SingleModeBreedersTeamMemberInfo charaInfo)
    {
        public string Name => Database.Names.GetCharacter(charaInfo.chara_id).Nickname;

        public string Rank => TurnInfoBreeders.TeamMemberRank[charaInfo.rank - 1];

        public string Explain => $"{charaInfo.Name}{charaInfo.Rank}{charaInfo.exp}";
    }
}

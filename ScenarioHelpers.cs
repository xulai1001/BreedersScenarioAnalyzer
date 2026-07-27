using System.Collections.Frozen;
using Gallop;
using UmamusumeResponseAnalyzer;

namespace BreedersScenarioAnalyzer;

public sealed class CommandInfo
{
    static readonly FrozenDictionary<int, int> DefaultTrainIndex = new Dictionary<int, int>
    {
        [101] = 0,
        [105] = 1,
        [102] = 2,
        [103] = 3,
        [106] = 4,
        [601] = 0,
        [602] = 1,
        [603] = 2,
        [604] = 3,
        [605] = 4
    }.ToFrozenDictionary();

    public CommandInfo(
        SingleModeBreedersCheckEventResponse.CommonResponse response,
        int commandId,
        IDictionary<int, int>? trainIndexDictionary = null)
    {
        CommandId = commandId;
        if ((trainIndexDictionary ?? DefaultTrainIndex).TryGetValue(commandId, out var trainIndex))
            TrainIndex = trainIndex + 1;

        var training = response.chara_info.training_level_info_array.FirstOrDefault(x => x.command_id == CommandId);
        TrainLevel = training is null ? 0 : training.level;

        var normalCommand = response.home_info.command_info_array.First(x => x.command_id == CommandId);
        TrainingPartners = normalCommand.training_partner_array
            .Select(x => new TrainingPartner(response, x, normalCommand))
            .OrderBy(x => x.Priority)
            .ToArray();
    }

    public int CommandId { get; }
    public int TrainIndex { get; }
    public int TrainLevel { get; }
    public IReadOnlyList<TrainingPartner> TrainingPartners { get; }
}

public sealed class TrainingPartner
{
    public TrainingPartner(
        SingleModeBreedersCheckEventResponse.CommonResponse response,
        int position,
        SingleModeCommandInfo command)
    {
        var rawName = position is >= 1 and <= 6
            ? Database.Names.GetSupportCard(
                response.chara_info.support_card_array.First(x => x.position == position).support_card_id).Nickname
            : Database.Names.GetCharacter(position).Nickname;
        var friendship = response.chara_info.evaluation_info_array.FirstOrDefault(x => x.target_id == position)?.evaluation ?? 0;

        Priority = position is >= 1 and <= 6 ? 0 : 1;
        Shining = position is >= 1 and <= 6 && friendship >= 80;
        Name = $"{rawName}{(friendship is > 0 and < 100 ? friendship.ToString() : string.Empty)}";
        if (command.tips_event_partner_array.Intersect(command.training_partner_array).Contains(position))
            Name = $"!{Name}";
    }

    public int Priority { get; }
    public string Name { get; }
    public bool Shining { get; }
}

public sealed class TrainStats
{
    public int[] FiveValueGain = [];
    public int PtGain;
    public int VitalGain;
    public int FailureRate;
}

public static class ScoreUtils
{
    public static int ReviseOver1200(int value) => value > 1200 ? value * 2 - 1200 : value;
}

public static class BreedersResponseExtensions
{
    public static int GetCommandInfoStage(this SingleModeBreedersCheckEventResponse response)
    {
        var data = response.data;
        var events = data.unchecked_event_array ?? [];
        if (data.chara_info.playing_state == 1 && events.Length == 0)
            return 2;
        if (data.chara_info.playing_state == 5 && events.Any(x => x.story_id == 400010112))
            return 5;
        if (data.chara_info.playing_state == 5 && events.Any(x => x.story_id == 830241003))
            return 3;
        return 0;
    }
}

public sealed class TurnInfoBreeders
{
    public static readonly int[] TrainIds = [101, 105, 102, 103, 106, 601, 602, 603, 604, 605];

    public static readonly FrozenDictionary<int, int> ToTrainId = new Dictionary<int, int>
    {
        [101] = 101,
        [105] = 105,
        [102] = 102,
        [103] = 103,
        [106] = 106,
        [601] = 101,
        [602] = 105,
        [603] = 102,
        [604] = 103,
        [605] = 106
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<int, int> ToTrainIndex = new Dictionary<int, int>
    {
        [101] = 0,
        [105] = 1,
        [102] = 2,
        [103] = 3,
        [106] = 4,
        [601] = 0,
        [602] = 1,
        [603] = 2,
        [604] = 3,
        [605] = 4
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<int, int> XiahesuIds = new Dictionary<int, int>
    {
        [101] = 601,
        [105] = 602,
        [102] = 603,
        [103] = 604,
        [106] = 605
    }.ToFrozenDictionary();

    public static readonly string[] TeamMemberRank =
    [
        "G", "F", "E", "D", "C", "B", "A", "S", "SS",
        "UG", "UF", "UE", "UD", "UC", "UB", "UA", "US"
    ];

    readonly SingleModeBreedersCheckEventResponse.CommonResponse response;

    public TurnInfoBreeders(SingleModeBreedersCheckEventResponse.CommonResponse response)
    {
        this.response = response;
        var breeders = response.breeders_data_set;
        CommandInfoArray = breeders.command_info_array
            .Where(x => x.command_type == 1)
            .Select(x => new CommandInfo(response, x.command_id, ToTrainIndex))
            .ToArray();
        CommandTeamMemberInfoDictionary = breeders.command_info_array.ToDictionary(x => x.command_id, x => x.team_member_info_array);
        SpecialTrainingStock = breeders.team_sp_training_info.stock_num;
        SpecialTrainingMax = breeders.team_sp_training_info.stock_max;
        SpecialTrainingActivated = breeders.team_sp_training_info.activated_state == 1;
        TeamMemberInfoDictionary = breeders.team_member_info_array.ToDictionary(x => x.chara_id, x => x);
    }

    public int Turn => response.chara_info.turn;
    public int Year => (Turn - 1) / 24 + 1;
    public int Month => ((Turn - 1) % 24) / 2 + 1;
    public string HalfMonth => Turn % 2 == 0 ? "后半" : "前半";
    public int Vital => response.chara_info.vital;
    public int MaxVital => response.chara_info.max_vital;
    public int[] Stats => [response.chara_info.speed, response.chara_info.stamina, response.chara_info.power, response.chara_info.guts, response.chara_info.wiz];
    public int[] StatsRevised => [.. Stats.Select(ScoreUtils.ReviseOver1200)];
    public int[] MaxStatsRevised =>
    [
        ScoreUtils.ReviseOver1200(response.chara_info.max_speed),
        ScoreUtils.ReviseOver1200(response.chara_info.max_stamina),
        ScoreUtils.ReviseOver1200(response.chara_info.max_power),
        ScoreUtils.ReviseOver1200(response.chara_info.max_guts),
        ScoreUtils.ReviseOver1200(response.chara_info.max_wiz)
    ];
    public IReadOnlyList<CommandInfo> CommandInfoArray { get; }
    public Dictionary<int, SingleModeBreedersTeamMemberInfo> TeamMemberInfoDictionary { get; }
    public Dictionary<int, SingleModeBreedersCommandTeamMemberInfo[]> CommandTeamMemberInfoDictionary { get; }
    public int SpecialTrainingStock { get; }
    public int SpecialTrainingMax { get; }
    public bool SpecialTrainingActivated { get; }
}

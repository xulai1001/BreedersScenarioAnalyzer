using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.TerminalGui;
using static BreedersScenarioAnalyzer.i18n.Game;

namespace BreedersScenarioAnalyzer;

public sealed class Handler
{
    static readonly int[] RequiredPoints = [10, 15, 15, 15, 10];

    int[] enhanceLevels = [];
    int currentTurn;

    public void Load(SingleModeBreedersLoadResponse response)
    {
        var groups = response.data.breeders_data_set_load?.enhance_group_array
            ?? throw new InvalidDataException("Breeders load response 缺少 enhance_group_array。");
        enhanceLevels = [.. groups.Select(x => x.level)];
    }

    public WorkspaceContent ParseBreederCommandInfo(SingleModeBreedersCheckEventResponse response)
    {
        var stage = response.GetCommandInfoStage();
        var data = response.data;
        var turn = new TurnInfoBreeders(data);
        var dataset = data.breeders_data_set;
        var importantRows = new List<string>();

        if (currentTurn != turn.Turn - 1
            && currentTurn != turn.Turn
            && turn.Turn != 1)
        {
            importantRows.Add(string.Format(I18N_WrongTurnAlert, currentTurn, turn.Turn));
        }

        if (data.chara_info.playing_state != 1)
            importantRows.Add(I18N_RepeatTurn);
        else
            currentTurn = turn.Turn;
        if (enhanceLevels.Length == 0 && turn.Turn >= 3)
            importantRows.Add("警告：缺少剧本 Buff 等级信息，需要从游戏主界面重新进入育成");

        var trainItems = new Dictionary<int, SingleModeCommandInfo>
        {
            [101] = data.home_info.command_info_array[0],
            [105] = data.home_info.command_info_array[1],
            [102] = data.home_info.command_info_array[2],
            [103] = data.home_info.command_info_array[3],
            [106] = data.home_info.command_info_array[4]
        };
        var trainStats = new TrainStats[5];
        var currentFiveValue = new[]
        {
            data.chara_info.speed,
            data.chara_info.stamina,
            data.chara_info.power,
            data.chara_info.guts,
            data.chara_info.wiz
        };
        var currentFiveValueRevised = currentFiveValue.Select(ScoreUtils.ReviseOver1200).ToArray();
        var totalValue = currentFiveValueRevised.Sum();

        for (var i = 0; i < 5; i++)
        {
            var trainId = TurnInfoBreeders.TrainIds[i];
            var trainParams = new Dictionary<int, int>
            {
                [1] = 0,
                [2] = 0,
                [3] = 0,
                [4] = 0,
                [5] = 0,
                [30] = 0,
                [10] = 0
            };
            foreach (var item in data.home_info.command_info_array)
            {
                if (!TurnInfoBreeders.ToTrainId.TryGetValue(item.command_id, out var value) || value != trainId)
                    continue;

                foreach (var trainParam in item.params_inc_dec_info_array)
                    trainParams[trainParam.target_type] += trainParam.value;
            }

            var stats = new TrainStats
            {
                FailureRate = trainItems[trainId].failure_rate,
                VitalGain = trainParams[10],
                FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]],
                PtGain = trainParams[30]
            };
            if (turn.Vital + stats.VitalGain > turn.MaxVital)
                stats.VitalGain = turn.MaxVital - turn.Vital;
            if (stats.VitalGain < -turn.Vital)
                stats.VitalGain = -turn.Vital;

            var valueGainUpper = dataset.command_info_array
                .FirstOrDefault(x => x.command_id == trainId || x.command_id == TurnInfoBreeders.XiahesuIds[trainId])
                ?.params_inc_dec_info_array;
            if (valueGainUpper is not null)
            {
                foreach (var item in valueGainUpper)
                {
                    if (item.target_type == 30)
                        stats.PtGain += item.value;
                    else if (item.target_type <= 5)
                        stats.FiveValueGain[item.target_type - 1] += item.value;
                }
            }

            for (var j = 0; j < 5; j++)
            {
                stats.FiveValueGain[j] =
                    ScoreUtils.ReviseOver1200(turn.Stats[j] + stats.FiveValueGain[j])
                    - ScoreUtils.ReviseOver1200(turn.Stats[j]);
            }

            trainStats[i] = stats;
        }

        var motivation = data.chara_info.motivation switch
        {
            5 => I18N_MotivationBest,
            4 => I18N_MotivationGood,
            3 => I18N_MotivationNormal,
            2 => I18N_MotivationBad,
            1 => I18N_MotivationWorst,
            _ => throw new InvalidOperationException($"未知干劲值: {data.chara_info.motivation}")
        };
        var availableTrainingCount = data.home_info.command_info_array.Count(x => x.is_enable == 1);
        if (availableTrainingCount <= 1)
            importantRows.Add($"非训练回合 playingState = {data.chara_info.playing_state}");
        if (data.chara_info.skill_point > 9500)
            importantRows.Add("剩余PT>9500（上限9999），请及时学习技能");

        var enhancePoint = dataset.predict_enhance_point + dataset.having_enhance_point;
        var requiredPoint = RequiredPoints[Math.Min((turn.Turn - 1) / 12, 4)];
        var lines = new List<string>
        {
            $"{turn.Year}{I18N_Year} {turn.Month}{I18N_Month}{turn.HalfMonth}"
            + $" | 总属性: {totalValue}, Pt: {data.chara_info.skill_point}"
            + $" | {I18N_Vital}: {turn.Vital}/{turn.MaxVital}"
            + $" | {motivation}",
            string.Empty,
            "== 重要信息 =="
        };
        lines.AddRange(importantRows.Count == 0 ? ["无"] : importantRows);
        lines.AddRange(
        [
            string.Empty,
            "== 剧本信息 ==",
            turn.SpecialTrainingActivated
                ? "SP训练: 启动"
                : $"SP训练: {turn.SpecialTrainingStock}/{turn.SpecialTrainingMax}",
            $"设施点数: {enhancePoint}/{requiredPoint}",
            enhanceLevels.Length == 0
                ? "设施等级: 未载入"
                : $"设施等级: {string.Join(" ", enhanceLevels)}",
            $"队伍评级: {TurnInfoBreeders.TeamMemberRank[dataset.team_rank - 1]}",
            string.Empty,
            "== 训练信息 =="
        ]);

        if (stage == 2)
        {
            var bestScore = trainStats.Max(x => x.FiveValueGain.Sum());
            foreach (var command in turn.CommandInfoArray)
            {
                var stats = trainStats[command.TrainIndex - 1];
                var score = stats.FiveValueGain.Sum();
                var trainingName = command.TrainIndex switch
                {
                    1 => I18N_Speed,
                    2 => I18N_Stamina,
                    3 => I18N_Power,
                    4 => I18N_Nuts,
                    5 => I18N_Wiz,
                    _ => throw new InvalidOperationException($"未知训练索引: {command.TrainIndex}")
                };
                var failureRate = stats.FailureRate > 0 ? $" ({stats.FailureRate}%)" : string.Empty;
                var currentStat = turn.StatsRevised[command.TrainIndex - 1];
                var statUpToMax = turn.MaxStatsRevised[command.TrainIndex - 1] - currentStat;
                var highlight = score == bestScore ? "▶ " : string.Empty;

                lines.Add($"{highlight}{trainingName}{failureRate} Lv{command.TrainLevel}");
                lines.Add($"  {I18N_CurrentRemainStat}: {currentStat}/{statUpToMax}");
                lines.Add($"  {I18N_StatSimple}: +{score} | Pt: +{stats.PtGain} | 体力: {stats.VitalGain:+#;-#;0}");

                var members = turn.CommandTeamMemberInfoDictionary[command.CommandId]
                    .Select(x => turn.TeamMemberInfoDictionary[x.chara_id].Explain);
                var partners = command.TrainingPartners
                    .Select(x => x.Shining ? $"★{x.Name}" : x.Name)
                    .Concat(members)
                    .ToArray();
                if (partners.Length > 0)
                    lines.Add($"  训练伙伴: {string.Join(", ", partners)}");
                lines.Add(string.Empty);
            }
        }
        else
        {
            lines.Add($"非训练阶段，stage={stage}");
        }

        var extraRows = new List<string>();
        foreach (var item in dataset.command_gain_exp_array)
        {
            var name = item.command_type switch
            {
                3 => "普通出行",
                4 => "比赛",
                7 => "休息",
                8 => "治病",
                _ => item.command_type.ToString()
            };
            extraRows.Add($"{name}: +{item.gain_exp}");
        }

        if (dataset.link_friend_outing_member_info_array is { Length: > 0 })
        {
            extraRows.Add("友人出行:");
            extraRows.AddRange(dataset.link_friend_outing_member_info_array.Select(
                x => $"  {Database.Names.GetCharacter(x.chara_id).Nickname}: +{x.gain_exp}"));
        }

        if (extraRows.Count > 0)
        {
            lines.Add("== Extras ==");
            lines.AddRange(extraRows);
        }

        return WorkspaceContent.Text(string.Join(Environment.NewLine, lines));
    }
}

using EventLoggerPlugin;
using Gallop;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Game;
using UmamusumeResponseAnalyzer.Game.TurnInfo;
using UmamusumeResponseAnalyzer.LocalizedLayout.Handlers;
using static BreedersScenarioAnalyzer.i18n.Game;

namespace BreedersScenarioAnalyzer
{
    public static class Handler
    {
        /// <summary>
        /// 载入游戏时保存的作战会议评价
        /// </summary>
        public static SingleModeBreederTeamReviewResult[] TeamReviewResult = [];
        /// <summary>
        /// 载入游戏时保存的设施等级
        /// </summary>
        public static SingleModeBreederEnhanceGroup[] EnhanceGroups = [];
        /// <summary>
        /// 每阶段需要攒的升级点数
        /// </summary>
        public static readonly int[] RequiredPoints = { 10, 15, 15, 15, 10 }; 
        public static void ParseBreederCommandInfo(SingleModeCheckEventResponse @event)
        {
            var stage = @event.GetCommandInfoStage();
            var layout = new Layout().SplitColumns(
                new Layout("Main").Size(CommandInfoLayout.Current.MainSectionWidth).SplitRows(
                    new Layout("体力干劲条").SplitColumns(
                        new Layout("日期").Ratio(4),
                        new Layout("总属性").Ratio(6),
                        new Layout("体力").Ratio(6),
                        new Layout("干劲").Ratio(3)).Size(3),
                    new Layout("重要信息").Size(5),
                    new Layout("剧本信息").SplitColumns(
                        new Layout("SP训练").Ratio(1),
                        new Layout("设施点数").Ratio(1),
                        new Layout("设施等级").Ratio(1),
                        new Layout("队伍评级").Ratio(1)
                        ).Size(3),
                    //new Layout("分割", new Rule()).Size(1),
                    new Layout("训练信息")  // size 20, 共约30行
                    ).Ratio(4),
                new Layout("Ext").Ratio(1)
                );
            var noTrainingTable = false;
            var critInfos = new List<string>();
            var turn = new TurnInfoBreeders(@event.data);
            
            //layout["SP训练"].Update(new Panel($"{string.Join(string.Empty, Enumerable.Repeat("●", turn.SpecialTrainingStock))}{(turn.SpecialTraningActivated ? "[green]●[/]" : string.Empty)}{string.Join(string.Empty, Enumerable.Repeat("○", turn.SpecialTrainingMax - turn.SpecialTrainingStock - (turn.SpecialTraningActivated ? 1 : 0)))}").Expand());
            var dataset = @event.data.breeders_data_set;
            var datasetLoad = @event.data.breeders_data_set_load;
            // SP训练
            if (turn.SpecialTrainingActivated)
            {
                layout["SP训练"].Update(new Panel($"[lime]SP训练启动[/]").Expand());
            }
            else
            {
                layout["SP训练"].Update(new Panel($"SP训练: {turn.SpecialTrainingStock} / {turn.SpecialTrainingMax}").Expand());
            }
            // 设施点数
            var enhancePoint = dataset.predict_enhance_point + dataset.having_enhance_point;
            var required = RequiredPoints[Math.Min((turn.Turn-1) / 12, 4)];
            var sty = new Style(foreground: Color.Red1);
            if (enhancePoint >= required + 5)
            {
                sty = new Style(foreground: Color.Lime);
            }
            else if (enhancePoint >= required)
            {
                sty = new Style(foreground: Color.Yellow);
            }
            layout["设施点数"].Update(new Panel(new Text($"当前升级点数: {enhancePoint}", sty)).Expand());
            // 设施等级
            if (datasetLoad != null)
            {
                TeamReviewResult = datasetLoad.team_review_result_array;
                EnhanceGroups = datasetLoad.enhance_group_array;
            }
            else if (EnhanceGroups.Count() == 0 && turn.Turn >= 3)
            {
                critInfos.Add($"[red]警告：缺少剧本Buff等级信息，需要从游戏主界面重新进入育成[/]");
            }
            if (EnhanceGroups.Count() > 0)
            {
                layout["设施等级"].Update(new Panel($"设施等级: {String.Join(" ", EnhanceGroups.Select(x => x.level))}").Expand());
            }
            // 队伍等级
            layout["队伍评级"].Update(new Panel($"队伍评级: {TurnInfoBreeders.TeamMemberRank[dataset.team_rank - 1]}").Expand());
            if (GameStats.currentTurn != turn.Turn - 1 //正常情况
                && GameStats.currentTurn != turn.Turn //重复显示
                && turn.Turn != 1 //第一个回合
                )
            {
                GameStats.isFullGame = false;
                critInfos.Add(string.Format(I18N_WrongTurnAlert, GameStats.currentTurn, turn.Turn));
                EventLogger.Init(@event);
                EventLogger.IsStart = true;
            }
            else if (turn.Turn == 1)
            {
                GameStats.isFullGame = true;
                EventLogger.Init(@event);
                EventLogger.IsStart = true;
            }

            //买技能，大师杯剧本年末比赛，会重复显示
            if (@event.data.chara_info.playing_state != 1)
            {
                critInfos.Add(I18N_RepeatTurn);
            }
            else
            {
                //初始化TurnStats
                GameStats.whichScenario = @event.data.chara_info.scenario_id;
                GameStats.currentTurn = turn.Turn;
                GameStats.stats[turn.Turn] = new TurnStats();
                EventLogger.Update(@event);
            }
            // T3 在EventLogger更新后需要开始捕获体力消耗
            //if (turn.Turn == 3)
            //{
            //    EventLogger.captureVitalSpending = true;
            //}
            var trainItems = new Dictionary<int, SingleModeCommandInfo>
            {
                { 101, @event.data.home_info.command_info_array[0] },
                { 105, @event.data.home_info.command_info_array[1] },
                { 102, @event.data.home_info.command_info_array[2] },
                { 103, @event.data.home_info.command_info_array[3] },
                { 106, @event.data.home_info.command_info_array[4] }
            };
            var trainStats = new TrainStats[5];
            var turnStat = @event.data.chara_info.playing_state != 1 ? new TurnStats() : GameStats.stats[turn.Turn];
            turnStat.motivation = @event.data.chara_info.motivation;
            var failureRate = new Dictionary<int, int>();

            // 总属性计算
            var currentFiveValue = new int[]
            {
                @event.data.chara_info.speed,
                @event.data.chara_info.stamina,
                @event.data.chara_info.power ,
                @event.data.chara_info.guts ,
                @event.data.chara_info.wiz ,
            };
            var fiveValueMaxRevised = new int[]
            {
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_speed),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_stamina),
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_power) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_guts) ,
                ScoreUtils.ReviseOver1200(@event.data.chara_info.max_wiz) ,
            };
            var currentFiveValueRevised = currentFiveValue.Select(ScoreUtils.ReviseOver1200).ToArray();
            var totalValue = currentFiveValueRevised.Sum();
            var totalValueWithPt = totalValue + @event.data.chara_info.skill_point;

            for (var i = 0; i < 5; i++)
            {
                var trainId = TurnInfoBreeders.TrainIds[i];
                failureRate[trainId] = trainItems[trainId].failure_rate;
                var trainParams = new Dictionary<int, int>()
                {
                    {1,0},
                    {2,0},
                    {3,0},
                    {4,0},
                    {5,0},
                    {30,0},
                    {10,0},
                };
                foreach (var item in turn.GetCommonResponse().home_info.command_info_array)
                {
                    if (TurnInfoBreeders.ToTrainId.TryGetValue(item.command_id, out var value) && value == trainId)
                    {
                        foreach (var trainParam in item.params_inc_dec_info_array)
                            trainParams[trainParam.target_type] += trainParam.value;
                    }
                }

                var stats = new TrainStats
                {
                    FailureRate = trainItems[trainId].failure_rate,
                    VitalGain = trainParams[10]
                };
                if (turn.Vital + stats.VitalGain > turn.MaxVital)
                    stats.VitalGain = turn.MaxVital - turn.Vital;
                if (stats.VitalGain < -turn.Vital)
                    stats.VitalGain = -turn.Vital;
                stats.FiveValueGain = [trainParams[1], trainParams[2], trainParams[3], trainParams[4], trainParams[5]];
                stats.PtGain = trainParams[30];

                var valueGainUpper = dataset.command_info_array.FirstOrDefault(x => x.command_id == trainId || x.command_id == TurnInfoBreeders.XiahesuIds[trainId])?.params_inc_dec_info_array;
                if (valueGainUpper != null)
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
                    stats.FiveValueGain[j] = ScoreUtils.ReviseOver1200(turn.Stats[j] + stats.FiveValueGain[j]) - ScoreUtils.ReviseOver1200(turn.Stats[j]);

                if (turn.Turn == 1)
                {
                    turnStat.trainLevel[i] = 1;
                    turnStat.trainLevelCount[i] = 0;
                }
                else
                {
                    var lastTrainLevel = GameStats.stats[turn.Turn - 1] != null ? GameStats.stats[turn.Turn - 1].trainLevel[i] : 1;
                    var lastTrainLevelCount = GameStats.stats[turn.Turn - 1] != null ? GameStats.stats[turn.Turn - 1].trainLevelCount[i] : 0;

                    turnStat.trainLevel[i] = lastTrainLevel;
                    turnStat.trainLevelCount[i] = lastTrainLevelCount;
                    if (GameStats.stats[turn.Turn - 1] != null &&
                        GameStats.stats[turn.Turn - 1].playerChoice == TurnInfoBreeders.TrainIds[i] &&
                        !GameStats.stats[turn.Turn - 1].isTrainingFailed &&
                        !((turn.Turn - 1 >= 37 && turn.Turn - 1 <= 40) || (turn.Turn - 1 >= 61 && turn.Turn - 1 <= 64))
                        )//上回合点的这个训练，计数+1
                        turnStat.trainLevelCount[i] += 1;
                    if (turnStat.trainLevelCount[i] >= 4)
                    {
                        turnStat.trainLevelCount[i] -= 4;
                        turnStat.trainLevel[i] += 1;
                    }
                    //检查是否有剧本全体训练等级+1
                    if (turn.Turn == 25 || turn.Turn == 37 || turn.Turn == 49)
                        turnStat.trainLevelCount[i] += 4;
                    if (turnStat.trainLevelCount[i] >= 4)
                    {
                        turnStat.trainLevelCount[i] -= 4;
                        turnStat.trainLevel[i] += 1;
                    }

                    if (turnStat.trainLevel[i] >= 5)
                    {
                        turnStat.trainLevel[i] = 5;
                        turnStat.trainLevelCount[i] = 0;
                    }

                    var trainlv = @event.data.chara_info.training_level_info_array.First(x => x.command_id == TurnInfoBreeders.TrainIds[i]).level;
                    if (turnStat.trainLevel[i] != trainlv && stage == 2)
                    {
                        //可能是半途开启小黑板，也可能是有未知bug
                        critInfos.Add($"[red]警告：训练等级预测错误，预测{TurnInfoBreeders.TrainIds[i]}为lv{turnStat.trainLevel[i]}(+{turnStat.trainLevelCount[i]})，实际为lv{trainlv}[/]");
                        turnStat.trainLevel[i] = trainlv;
                        turnStat.trainLevelCount[i] = 0;//如果是半途开启小黑板，则会在下一次升级时变成正确的计数
                    }
                }

                trainStats[i] = stats;
            }
            if (stage == 2)
            {
                // 把训练等级信息更新到GameStats
                turnStat.fiveTrainStats = trainStats;
                GameStats.stats[turn.Turn] = turnStat;
            }

            //训练或比赛阶段
            if (stage == 2)
            {
                var grids = new Grid();
                grids.AddColumns(6);
                foreach (var column in grids.Columns)
                {
                    column.Padding = new Padding(0, 0, 0, 0);
                }

                var failureRateStr = new string[5];
                //失败率>=40%标红、>=20%(有可能大失败)标DarkOrange、>0%标黄
                for (var i = 0; i < 5; i++)
                {
                    var thisFailureRate = failureRate[TurnInfoBreeders.TrainIds[i]];
                    failureRateStr[i] = thisFailureRate switch
                    {
                        >= 40 => $"[red]({thisFailureRate}%)[/]",
                        >= 20 => $"[darkorange]({thisFailureRate}%)[/]",
                        > 0 => $"[yellow]({thisFailureRate}%)[/]",
                        _ => string.Empty
                    };
                }

                var commands = turn.CommandInfoArray.Select(command =>
                {
                    var table = new Table()
                    .AddColumn(command.TrainIndex switch
                    {
                        1 => $"{I18N_Speed}{failureRateStr[0]}",
                        2 => $"{I18N_Stamina}{failureRateStr[1]}",
                        3 => $"{I18N_Power}{failureRateStr[2]}",
                        4 => $"{I18N_Nuts}{failureRateStr[3]}",
                        5 => $"{I18N_Wiz}{failureRateStr[4]}"
                    });

                    var currentStat = turn.StatsRevised[command.TrainIndex - 1];
                    var statUpToMax = turn.MaxStatsRevised[command.TrainIndex - 1] - currentStat;
                    table.AddRow(I18N_CurrentRemainStat);
                    table.AddRow($"{currentStat}:{statUpToMax switch
                    {
                        > 400 => $"{statUpToMax}",
                        > 200 => $"[yellow]{statUpToMax}[/]",
                        _ => $"[red]{statUpToMax}[/]"
                    }}");
                    table.AddRow(new Rule());

                    var members = turn.CommandTeamMemberInfoDictionary[command.CommandId].Select(x => turn.TeamMemberInfoDictionary[x.chara_id]);

                    table.AddRow($"Lv{command.TrainLevel}");
                    table.AddRow(new Rule());

                    var stats = trainStats[command.TrainIndex - 1];
                    var score = stats.FiveValueGain.Sum();
                    if (score == trainStats.Max(x => x.FiveValueGain.Sum()))
                        table.AddRow($"{I18N_StatSimple}:[aqua]{score}[/]|Pt:{stats.PtGain}");
                    else
                        table.AddRow($"{I18N_StatSimple}:{score}|Pt:{stats.PtGain}");

                    var personCount = command.TrainingPartners.Count() + members.Count();
                    foreach (var trainingPartner in command.TrainingPartners)
                    {
                        table.AddRow(trainingPartner.Name);
                        if (trainingPartner.Shining)
                            table.BorderColor(Color.LightGreen);
                    }
                    // 同时列出小头, 总行数增加到8
                    foreach (var m in members)
                        table.AddRow(m.Explain);
                    for (var i = 8 - personCount; i > 0; i--)
                    {
                        table.AddRow(string.Empty);
                    }
                    table.AddRow(new Rule());

                    return new Padder(table).Padding(0, 0, 0, 0);
                }).ToList();
                grids.AddRow([.. commands]);

                layout["训练信息"].Update(grids);
            }
            else
            {
                var grids = new Grid();
                grids.AddColumns(1);
                grids.AddRow([$"非训练阶段，stage={stage}"]);
                layout["训练信息"].Update(grids);
                noTrainingTable = true;
            }

            // 额外信息
            var exTable = new Table().AddColumn("Extras");
            exTable.HideHeaders();
            // 计算连续事件表现
            var eventPerf = EventLogger.PrintCardEventPerf(@event.data.chara_info.scenario_id);
            if (eventPerf.Count > 0)
            {
                exTable.AddRow(new Rule());
                foreach (var row in eventPerf)
                    exTable.AddRow(new Markup(row));
            }
            // 其他动作信息
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
                exTable.AddRow($"{name}: +{item.gain_exp}");
            }
            if (dataset.link_friend_outing_member_info_array != null && dataset.link_friend_outing_member_info_array.Count() > 0)
            {
                exTable.AddRow("友人出行:");
                foreach (var item in dataset.link_friend_outing_member_info_array)
                {
                    exTable.AddRow($"{Database.Names.GetCharacter(item.chara_id).Nickname}: +{item.gain_exp}");
                }
            }

            layout["日期"].Update(new Panel($"{turn.Year}{I18N_Year} {turn.Month}{I18N_Month}{turn.HalfMonth}").Expand());
            layout["总属性"].Update(new Panel($"[cyan]总属性: {totalValue}, Pt: {@event.data.chara_info.skill_point}[/]").Expand());
            layout["体力"].Update(new Panel($"{I18N_Vital}: [green]{turn.Vital}[/]/{turn.MaxVital}").Expand());
            layout["干劲"].Update(new Panel(@event.data.chara_info.motivation switch
            {
                // 换行分裂和箭头符号有关，去掉
                5 => $"[green]{I18N_MotivationBest}[/]",
                4 => $"[yellow]{I18N_MotivationGood}[/]",
                3 => $"[red]{I18N_MotivationNormal}[/]",
                2 => $"[red]{I18N_MotivationBad}[/]",
                1 => $"[red]{I18N_MotivationWorst}[/]"
            }).Expand());

            var availableTrainingCount = @event.data.home_info.command_info_array.Count(x => x.is_enable == 1);
            if (availableTrainingCount <= 1)
            {
                critInfos.Add($"[aqua]非训练回合 playingState = {@event.data.chara_info.playing_state}[/]");
            }
            if (@event.data.chara_info.skill_point > 9500)
            {
                critInfos.Add("[red]剩余PT>9500（上限9999），请及时学习技能");
            }
            layout["重要信息"].Update(new Panel(string.Join(Environment.NewLine, critInfos)).Expand());

            layout["Ext"].Update(exTable);

            GameStats.Print();

            AnsiConsole.Write(layout);
            // 光标倒转一点
            if (noTrainingTable)
                AnsiConsole.Cursor.SetPosition(0, 15);
            else
                AnsiConsole.Cursor.SetPosition(0, 35);
        }
    }
}

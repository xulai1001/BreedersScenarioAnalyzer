# BreedersScenarioAnalyzer

梦想杯训练分析结果显示在插件 workspace 中。

插件按 `(single_mode_chara_id, turn)` 保存本次进程内的历史快照；同一键再次发布时原位更新。使用 `↑`、`↓` 查看相邻的较旧、较新记录，使用 `←`、`→` 跳到最旧、最新记录。`PageUp`、`PageDown`、`Home`、`End` 和鼠标滚轮用于滚动当前记录。

`PluginData/BreedersScenarioAnalyzer/settings.json` 中的 `historyLimit` 控制最多保留的记录数，默认值为 `100`，有效范围为 `0` 到 `1000`。设为 `0` 时只显示最新分析结果，不保留历史，也不接管方向键。配置只在选择“保存”后写入；历史内容不跨插件重启保存。

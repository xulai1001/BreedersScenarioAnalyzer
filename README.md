# BreedersScenarioAnalyzer

梦想杯训练分析结果显示在插件 workspace 中。

插件按 `(single_mode_chara_id, turn)` 保存本次进程内的历史快照；同一键再次发布时原位更新。使用 `↑`、`↓` 查看相邻的较旧、较新记录，使用 `←`、`→` 跳到最旧、最新记录。`PageUp`、`PageDown`、`Home`、`End` 和鼠标滚轮用于滚动当前记录。

`PluginData/BreedersScenarioAnalyzer/settings.json` 中的 `historyLimit` 控制最多保留的记录数，默认值为 `100`，有效范围为 `0` 到 `1000`。设为 `0` 时只显示最新分析结果，不保留历史，也不接管方向键。配置只在选择“保存”后写入；历史内容不跨插件重启保存。

## 构建

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\BreedersScenarioAnalyzer.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。

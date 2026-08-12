using System.Text.Json;
using System.Text.Json.Serialization;

namespace BreedersScenarioAnalyzer;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class HistorySettings
{
    internal const int DefaultLimit = 100;
    internal const int MaximumLimit = 1000;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [JsonRequired]
    public int HistoryLimit { get; init; }

    internal static HistorySettings Load(string path)
    {
        if (!File.Exists(path))
            return new() { HistoryLimit = DefaultLimit };

        try
        {
            var settings = JsonSerializer.Deserialize<HistorySettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"梦想杯剧本解析器配置文件为空或格式无效: {path}");
            Validate(settings.HistoryLimit, path);
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"梦想杯剧本解析器配置文件无效: {path}。{ex.Message}", ex);
        }
    }

    internal void Save(string path)
    {
        Validate(HistoryLimit, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    internal static bool IsValid(int value) => value is >= 0 and <= MaximumLimit;

    static void Validate(int value, string path)
    {
        if (!IsValid(value))
        {
            throw new InvalidDataException(
                $"梦想杯剧本解析器配置文件中的 historyLimit 必须在 0 到 {MaximumLimit} 之间: {path}");
        }
    }
}

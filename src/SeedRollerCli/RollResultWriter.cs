using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace SeedRollerCli;

internal sealed class RollResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OptionFilter _filter;
    private readonly string _outputPath;
    private readonly List<SerializableSeedResult> _entries = new();
    private int _totalSeeds;
    private int _matchedOptions;

    public RollResultWriter(OptionFilter filter, string? outputPath)
    {
        _filter = filter;
        _outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? SeedRollerDefaults.DefaultResultJson
            : outputPath;
    }

    public int TotalSeeds => _totalSeeds;

    public int MatchedSeedCount => _entries.Count;

    public int MatchedOptionCount => _matchedOptions;

    public string OutputPath => Path.GetFullPath(_outputPath);

    public bool Process(SeedRollResult result)
    {
        _totalSeeds++;
        var options = BuildOptions(result);
        if (options.Count == 0)
        {
            return false;
        }

        _entries.Add(new SerializableSeedResult(result.Seed, options));
        _matchedOptions += options.Count;
        return true;
    }

    public void Complete(bool canceled = false)
    {
        var fullPath = OutputPath;
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var results = new RollResults(
            DateTimeOffset.Now,
            _totalSeeds,
            _entries.Count,
            _matchedOptions,
            _entries);

        using (var stream = File.Create(fullPath))
        {
            JsonSerializer.Serialize(stream, results, JsonOptions);
        }

        if (_entries.Count == 0 && _filter.HasCriteria)
        {
            RollerLog.Warning($"共检查 {_totalSeeds} 个种子，但当前筛选条件没有命中。已生成空的结果文件：{fullPath}");
            RollerLog.Info("提示：可调整 config.json 中的筛选条件后重试。");
            return;
        }

        if (canceled)
        {
            RollerLog.Warning($"用户取消了 roll，已处理 {_totalSeeds} 个种子，命中 {_entries.Count} 条结果（{_matchedOptions} 个选项）。当前进度已保存到：{fullPath}");
            return;
        }

        RollerLog.Info($"共检查 {_totalSeeds} 个种子，其中 {_entries.Count} 个符合条件（共 {_matchedOptions} 个选项）。结果已保存到：{fullPath}");
    }

    private List<SerializableOption> BuildOptions(SeedRollResult result)
    {
        var options = new List<SerializableOption>();
        foreach (var option in result.Options)
        {
            var (title, description) = LocalizationProvider.GetRelicStrings(option.ModelId, option.DisplayName);
            if (_filter.HasCriteria && !_filter.Matches(option, title, description))
            {
                continue;
            }

            var details = option.Details.ToList();
            options.Add(new SerializableOption(
                option.Kind.ToString(),
                option.ModelId.Entry,
                title,
                description,
                option.Note,
                details));
        }

        return options;
    }
}

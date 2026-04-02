using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SeedRollerCli;

namespace SeedRollerUI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int MaxLogEntries = 400;

    private readonly IReadOnlyList<EnumOption<CharacterChoice>> _characterOptions =
    [
        new(CharacterChoice.Ironclad, "铁卫"),
        new(CharacterChoice.Silent, "沉默猎手"),
        new(CharacterChoice.Regent, "摄政者"),
        new(CharacterChoice.Necrobinder, "通灵匠"),
        new(CharacterChoice.Defect, "故障机器人")
    ];

    private readonly IReadOnlyList<EnumOption<SeedMode>> _seedModeOptions =
    [
        new(SeedMode.Random, "随机"),
        new(SeedMode.Incremental, "递增（顺延）")
    ];

    private readonly IReadOnlyList<EnumOption<NeowChoiceKind?>> _filterKindOptions =
    [
        new(null, "全部"),
        new(NeowChoiceKind.Positive, "正向"),
        new(NeowChoiceKind.Curse, "代价")
    ];

    private readonly ObservableCollection<LogEntry> _logs = new();

    private CancellationTokenSource? _cts;
    private string _gameDataPath = SeedRollerDefaults.DefaultGameDataPath;
    private string? _seedInfoPath = Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultSeedInfo);
    private CharacterChoice _selectedCharacter = CharacterChoice.Defect;
    private SeedMode _selectedSeedMode = SeedMode.Random;
    private string _startSeed = SeedRollerDefaults.DefaultSeed;
    private string _countText = "20";
    private string _ascensionText = "0";
    private NeowChoiceKind? _selectedFilterKind;
    private string _filterRelicTerms = string.Empty;
    private string _filterRelicIds = string.Empty;
    private string _filterCardIds = string.Empty;
    private string _filterPotionIds = string.Empty;
    private string _resultJsonPath = Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultResultJson);
    private bool _isRunning;
    private FilterInputMode _relicFilterMode = FilterInputMode.Keyword;
    private int _processedSeeds;
    private int _matchedSeeds;
    private int _matchedOptions;
    private string? _statusMessage = "尚未开始";
    private string? _lastResultPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LogEntry> Logs => _logs;

    public IReadOnlyList<EnumOption<CharacterChoice>> CharacterOptions => _characterOptions;

    public IReadOnlyList<EnumOption<SeedMode>> SeedModeOptions => _seedModeOptions;

    public IReadOnlyList<EnumOption<NeowChoiceKind?>> FilterKindOptions => _filterKindOptions;

    public string GameDataPath
    {
        get => _gameDataPath;
        set => SetProperty(ref _gameDataPath, value);
    }

    public string? SeedInfoPath
    {
        get => _seedInfoPath;
        set => SetProperty(ref _seedInfoPath, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public CharacterChoice SelectedCharacter
    {
        get => _selectedCharacter;
        set => SetProperty(ref _selectedCharacter, value);
    }

    public SeedMode SelectedSeedMode
    {
        get => _selectedSeedMode;
        set
        {
            if (SetProperty(ref _selectedSeedMode, value))
            {
                OnPropertyChanged(nameof(IsIncrementalMode));
            }
        }
    }

    public bool IsIncrementalMode => SelectedSeedMode == SeedMode.Incremental;

    public string StartSeed
    {
        get => _startSeed;
        set => SetProperty(ref _startSeed, value ?? string.Empty);
    }

    public string CountText
    {
        get => _countText;
        set => SetProperty(ref _countText, value ?? string.Empty);
    }

    public string AscensionText
    {
        get => _ascensionText;
        set => SetProperty(ref _ascensionText, value ?? string.Empty);
    }

    public NeowChoiceKind? SelectedFilterKind
    {
        get => _selectedFilterKind;
        set => SetProperty(ref _selectedFilterKind, value);
    }

    public string FilterRelicTerms
    {
        get => _filterRelicTerms;
        set => SetProperty(ref _filterRelicTerms, value ?? string.Empty);
    }

    public string FilterRelicIds
    {
        get => _filterRelicIds;
        set => SetProperty(ref _filterRelicIds, value ?? string.Empty);
    }

    public bool IsRelicKeywordMode
    {
        get => _relicFilterMode == FilterInputMode.Keyword;
        set
        {
            if (value)
            {
                SetRelicFilterMode(FilterInputMode.Keyword);
            }
        }
    }

    public bool IsRelicIdMode
    {
        get => _relicFilterMode == FilterInputMode.Id;
        set
        {
            if (value)
            {
                SetRelicFilterMode(FilterInputMode.Id);
            }
        }
    }

    public string FilterCardIds
    {
        get => _filterCardIds;
        set => SetProperty(ref _filterCardIds, value ?? string.Empty);
    }

    public string FilterPotionIds
    {
        get => _filterPotionIds;
        set => SetProperty(ref _filterPotionIds, value ?? string.Empty);
    }

    public string ResultJsonPath
    {
        get => _resultJsonPath;
        set => SetProperty(ref _resultJsonPath, value ?? string.Empty);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanStart => !IsRunning;

    public bool CanCancel => IsRunning;

    public int ProcessedSeeds
    {
        get => _processedSeeds;
        private set => SetProperty(ref _processedSeeds, value);
    }

    public int MatchedSeeds
    {
        get => _matchedSeeds;
        private set => SetProperty(ref _matchedSeeds, value);
    }

    public int MatchedOptions
    {
        get => _matchedOptions;
        private set => SetProperty(ref _matchedOptions, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? LastResultPath
    {
        get => _lastResultPath;
        private set
        {
            if (SetProperty(ref _lastResultPath, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrWhiteSpace(LastResultPath) && File.Exists(LastResultPath);

    public void ClearLogs()
    {
        _logs.Clear();
    }

    public void AppendLog(RollerLogLevel level, string message)
    {
        while (_logs.Count >= MaxLogEntries)
        {
            _logs.RemoveAt(0);
        }

        _logs.Add(new LogEntry(DateTimeOffset.Now, level, message));
    }

    public async Task StartRollAsync(Dispatcher dispatcher)
    {
        if (IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GameDataPath) || !Directory.Exists(GameDataPath))
        {
            AppendLog(RollerLogLevel.Error, "游戏 data 路径无效，请先选择正确的 data 目录。");
            StatusMessage = "缺少 data 路径";
            return;
        }

        var settings = BuildSettings();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProcessedSeeds = 0;
        MatchedSeeds = 0;
        MatchedOptions = 0;
        LastResultPath = null;
        StatusMessage = "正在 roll 种子……";
        ClearLogs();
        AppendLog(RollerLogLevel.Info, "开始 roll，正在初始化游戏数据……");

        try
        {
            var progress = new Progress<RollerProgress>(UpdateProgress);
            var logSink = new DispatcherLogSink(dispatcher, AppendLog);
            var runner = new SeedRollerRunner();
            var summary = await Task.Run(() => runner.Run(settings, progress, logSink, _cts.Token));

            LastResultPath = summary.ResultJsonPath;
            StatusMessage = $"完成：共 {summary.ProcessedSeeds} 个种子，命中 {summary.MatchedSeeds}（{summary.MatchedOptions} 个选项）。";
            AppendLog(RollerLogLevel.Info, $"结果已保存到：{summary.ResultJsonPath}");
        }
        catch (OperationCanceledException)
        {
            AppendLog(RollerLogLevel.Warning, "已取消 roll。");
            StatusMessage = "用户取消。";
        }
        catch (Exception ex)
        {
            AppendLog(RollerLogLevel.Error, $"运行失败：{ex.Message}");
            StatusMessage = "运行失败";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    public void CancelRoll()
    {
        _cts?.Cancel();
    }

    public void ApplyConfig(RollerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.GameDataPath))
        {
            GameDataPath = config.GameDataPath!;
        }

        if (!string.IsNullOrWhiteSpace(config.SeedInfoPath))
        {
            SeedInfoPath = config.SeedInfoPath!;
        }

        if (!string.IsNullOrWhiteSpace(config.StartSeed))
        {
            StartSeed = config.StartSeed!;
        }

        if (config.Count.HasValue)
        {
            CountText = config.Count.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (config.Ascension.HasValue)
        {
            AscensionText = Math.Max(0, config.Ascension.Value).ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(config.Mode))
        {
            SelectedSeedMode = ParseSeedMode(config.Mode);
        }

        if (!string.IsNullOrWhiteSpace(config.Character))
        {
            SelectedCharacter = ParseCharacter(config.Character);
        }

        if (!string.IsNullOrWhiteSpace(config.ResultJson))
        {
            ResultJsonPath = config.ResultJson!;
        }

        if (config.Filter is { } filter)
        {
            SelectedFilterKind = ParseFilterKind(filter.Kind);
            FilterRelicTerms = JoinTerms(filter.RelicTerms);
            FilterRelicIds = JoinTerms(filter.RelicIds);
            FilterCardIds = JoinTerms(filter.CardIds);
            FilterPotionIds = JoinTerms(filter.PotionIds);
            if (filter.RelicIds != null && filter.RelicIds.Length > 0)
            {
                SetRelicFilterMode(FilterInputMode.Id);
            }
            else
            {
                SetRelicFilterMode(FilterInputMode.Keyword);
            }
        }

        StatusMessage = "配置已载入，可直接运行。";
    }

    public void LoadConfigFromFile(string path)
    {
        var config = ConfigLoader.Load(path);
        ApplyConfig(config);
        AppendLog(RollerLogLevel.Info, $"已加载配置：{path}");
    }

    public bool TryGetResultPath(out string? path)
    {
        path = LastResultPath;
        return HasResult;
    }

    public RollerConfig ExportConfig()
    {
        var filter = new RollerFilterConfig
        {
            Kind = FilterKindToString(SelectedFilterKind),
            RelicTerms = IsRelicKeywordMode ? ParseTerms(FilterRelicTerms).ToArray() : Array.Empty<string>(),
            RelicIds = IsRelicIdMode ? ParseTerms(FilterRelicIds).ToArray() : Array.Empty<string>(),
            CardIds = ParseTerms(FilterCardIds).ToArray(),
            PotionIds = ParseTerms(FilterPotionIds).ToArray()
        };

        return new RollerConfig
        {
            GameDataPath = NormalizeOrNull(GameDataPath),
            SeedInfoPath = NormalizeOrNull(SeedInfoPath),
            StartSeed = NormalizeOrNull(StartSeed),
            Count = ParsePositiveIntOrNull(CountText),
            Mode = SelectedSeedMode == SeedMode.Incremental ? "incremental" : "random",
            Character = CharacterToString(SelectedCharacter),
            Ascension = ParseNonNegativeIntOrNull(AscensionText),
            ResultJson = NormalizeOrNull(ResultJsonPath),
            Filter = filter
        };
    }

    private void UpdateProgress(RollerProgress progress)
    {
        ProcessedSeeds = progress.ProcessedSeeds;
        MatchedSeeds = progress.MatchedSeeds;
        MatchedOptions = progress.MatchedOptions;
    }

    private RollerSettings BuildSettings()
    {
        return new RollerSettings
        {
            GameDataPath = GameDataPath.Trim(),
            Count = ParsePositiveInt(CountText, 20),
            StartSeed = string.IsNullOrWhiteSpace(StartSeed) ? SeedRollerDefaults.DefaultSeed : StartSeed.Trim(),
            Mode = SelectedSeedMode,
            Character = SelectedCharacter,
            Ascension = ParseNonNegativeInt(AscensionText, 0),
            ResultJsonPath = string.IsNullOrWhiteSpace(ResultJsonPath) ? SeedRollerDefaults.DefaultResultJson : ResultJsonPath.Trim(),
            SeedInfoPath = string.IsNullOrWhiteSpace(SeedInfoPath) ? null : SeedInfoPath
        }.WithFilter(builder =>
        {
            builder.Kind = SelectedFilterKind;
            builder.RelicTerms.AddRange(IsRelicKeywordMode ? ParseTerms(FilterRelicTerms) : Array.Empty<string>());
            builder.RelicIds.AddRange(IsRelicIdMode ? ParseTerms(FilterRelicIds) : Array.Empty<string>());
            builder.CardIds.AddRange(ParseTerms(FilterCardIds));
            builder.PotionIds.AddRange(ParseTerms(FilterPotionIds));
        });
    }

    private static IEnumerable<string> ParseTerms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(new[] { '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string JoinTerms(string[]? items)
    {
        if (items == null || items.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, items);
    }

    private static int ParsePositiveInt(string? text, int fallback)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        return fallback;
    }

    private static int? ParsePositiveIntOrNull(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        return null;
    }

    private static int ParseNonNegativeInt(string? text, int fallback)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0)
        {
            return value;
        }

        return fallback;
    }

    private static int? ParseNonNegativeIntOrNull(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0)
        {
            return value;
        }

        return null;
    }

    private static SeedMode ParseSeedMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "incremental" or "inc" or "sequential" => SeedMode.Incremental,
            _ => SeedMode.Random
        };
    }

    private static CharacterChoice ParseCharacter(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "silent" => CharacterChoice.Silent,
            "regent" => CharacterChoice.Regent,
            "necrobinder" => CharacterChoice.Necrobinder,
            "defect" => CharacterChoice.Defect,
            _ => CharacterChoice.Ironclad
        };
    }

    private static NeowChoiceKind? ParseFilterKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "positive" or "pos" or "p" => NeowChoiceKind.Positive,
            "curse" or "cost" or "negative" or "c" => NeowChoiceKind.Curse,
            _ => null
        };
    }

    private static string? FilterKindToString(NeowChoiceKind? kind)
    {
        return kind?.ToString().ToLowerInvariant();
    }

    private static string CharacterToString(CharacterChoice choice)
    {
        return choice switch
        {
            CharacterChoice.Silent => "silent",
            CharacterChoice.Regent => "regent",
            CharacterChoice.Necrobinder => "necrobinder",
            CharacterChoice.Defect => "defect",
            _ => "ironclad"
        };
    }

    private static string? NormalizeOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void SetRelicFilterMode(FilterInputMode mode)
    {
        if (_relicFilterMode != mode)
        {
            _relicFilterMode = mode;
            OnPropertyChanged(nameof(IsRelicKeywordMode));
            OnPropertyChanged(nameof(IsRelicIdMode));
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DispatcherLogSink(Dispatcher dispatcher, Action<RollerLogLevel, string> callback) : IRollerLogSink
    {
        public void Log(RollerLogLevel level, string message)
        {
            dispatcher.Invoke(() => callback(level, message));
        }
    }
}

public sealed record LogEntry(DateTimeOffset Timestamp, RollerLogLevel Level, string Message)
{
    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText => Level switch
    {
        RollerLogLevel.Info => "信息",
        RollerLogLevel.Warning => "警告",
        RollerLogLevel.Error => "错误",
        _ => "日志"
    };
}

public sealed record EnumOption<T>(T Value, string DisplayName);

public enum FilterInputMode
{
    Keyword,
    Id
}

file static class RollerSettingsExtensions
{
    public static RollerSettings WithFilter(this RollerSettings settings, Action<RollerFilterSettings> apply)
    {
        apply(settings.Filter);
        return settings;
    }
}

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
using System.Windows.Data;
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
        new(SeedMode.Incremental, "递增（顺延）"),
        new(SeedMode.UntilMatch, "命中即停")
    ];

    private readonly IReadOnlyList<string> _ascensionOptions =
        Enumerable.Range(0, 11)
            .Select(i => i.ToString(CultureInfo.InvariantCulture))
            .ToArray();

    private readonly ICollectionView _relicView;
    private readonly ICollectionView _cardView;
    private readonly ICollectionView _potionView;
    private readonly ObservableCollection<LogEntry> _logs = new();
    private readonly ObservableCollection<SelectableOption> _availableRelics = new();
    private readonly ObservableCollection<SelectableOption> _availableCards = new();
    private readonly ObservableCollection<SelectableOption> _availablePotions = new();
    private readonly ObservableCollection<SelectableOption> _selectedRelics = new();
    private readonly ObservableCollection<SelectableOption> _selectedCards = new();
    private readonly ObservableCollection<SelectableOption> _selectedPotions = new();

    private CancellationTokenSource? _cts;
    private string _gameDataPath = SeedRollerDefaults.DefaultGameDataPath;
    private CharacterChoice _selectedCharacter = CharacterChoice.Defect;
    private SeedMode _selectedSeedMode = SeedMode.Random;
    private string _startSeed = SeedRollerDefaults.DefaultSeed;
    private string _countText = "20";
    private string _ascensionText = "0";
    private string _resultJsonPath = Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultResultJson);
    private string _relicSearchText = string.Empty;
    private string _cardSearchText = string.Empty;
    private string _potionSearchText = string.Empty;
    private bool _isRunning;
    private int _processedSeeds;
    private int _matchedSeeds;
    private int _matchedOptions;
    private string? _statusMessage = "尚未开始";
    private string? _lastResultPath;
    private SelectableOption? _relicToAdd;
    private SelectableOption? _cardToAdd;
    private SelectableOption? _potionToAdd;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _relicView = CollectionViewSource.GetDefaultView(_availableRelics);
        _relicView.Filter = RelicFilter;
        _cardView = CollectionViewSource.GetDefaultView(_availableCards);
        _cardView.Filter = CardFilter;
        _potionView = CollectionViewSource.GetDefaultView(_availablePotions);
        _potionView.Filter = PotionFilter;

        LoadSeedInfoOptions();
    }

    private void LoadSeedInfoOptions()
    {
        _availableRelics.Clear();
        _availableCards.Clear();
        _availablePotions.Clear();

        var catalog = SeedInfoCatalogProvider.EnsureLoaded();
        if (catalog == null)
        {
            StatusMessage = "未找到 seed_info 数据，输出名称将使用默认 ID";
            return;
        }

        foreach (var option in (catalog.Options ?? Array.Empty<SeedInfoOption>())
                     .Where(o => !string.IsNullOrWhiteSpace(o.RelicId))
                     .OrderBy(o => o.Title ?? o.RelicId, StringComparer.OrdinalIgnoreCase))
        {
            _availableRelics.Add(new SelectableOption(option.RelicId!, FormatRelicLabel(option)));
        }

        foreach (var card in (catalog.Cards ?? Array.Empty<SeedInfoEntry>())
                     .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                     .OrderBy(c => c.Name ?? c.Id, StringComparer.OrdinalIgnoreCase))
        {
            _availableCards.Add(new SelectableOption(card.Id, FormatEntryLabel(card)));
        }

        foreach (var potion in (catalog.Potions ?? Array.Empty<SeedInfoEntry>())
                     .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                     .OrderBy(p => p.Name ?? p.Id, StringComparer.OrdinalIgnoreCase))
        {
            _availablePotions.Add(new SelectableOption(potion.Id, FormatEntryLabel(potion)));
        }

        RelicToAdd = _availableRelics.FirstOrDefault();
        CardToAdd = _availableCards.FirstOrDefault();
        PotionToAdd = _availablePotions.FirstOrDefault();

        _relicView.Refresh();
        _cardView.Refresh();
        _potionView.Refresh();
    }

    public ObservableCollection<LogEntry> Logs => _logs;

    public ICollectionView AvailableRelicsView => _relicView;

    public ICollectionView AvailableCardsView => _cardView;

    public ICollectionView AvailablePotionsView => _potionView;

    public ObservableCollection<SelectableOption> SelectedRelics => _selectedRelics;

    public ObservableCollection<SelectableOption> SelectedCards => _selectedCards;

    public ObservableCollection<SelectableOption> SelectedPotions => _selectedPotions;

    public IReadOnlyList<EnumOption<CharacterChoice>> CharacterOptions => _characterOptions;

    public IReadOnlyList<EnumOption<SeedMode>> SeedModeOptions => _seedModeOptions;

    public IReadOnlyList<string> AscensionOptions => _ascensionOptions;

    public string GameDataPath
    {
        get => _gameDataPath;
        set => SetProperty(ref _gameDataPath, value);
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

    public bool IsIncrementalMode => SelectedSeedMode != SeedMode.Random;

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

    public string RelicSearchText
    {
        get => _relicSearchText;
        set
        {
            if (SetProperty(ref _relicSearchText, value ?? string.Empty))
            {
                _relicView.Refresh();
            }
        }
    }

    public string CardSearchText
    {
        get => _cardSearchText;
        set
        {
            if (SetProperty(ref _cardSearchText, value ?? string.Empty))
            {
                _cardView.Refresh();
            }
        }
    }

    public string PotionSearchText
    {
        get => _potionSearchText;
        set
        {
            if (SetProperty(ref _potionSearchText, value ?? string.Empty))
            {
                _potionView.Refresh();
            }
        }
    }

    public string ResultJsonPath
    {
        get => _resultJsonPath;
        set => SetProperty(ref _resultJsonPath, value ?? string.Empty);
    }

    public SelectableOption? RelicToAdd
    {
        get => _relicToAdd;
        set => SetProperty(ref _relicToAdd, value);
    }

    public SelectableOption? CardToAdd
    {
        get => _cardToAdd;
        set => SetProperty(ref _cardToAdd, value);
    }

    public SelectableOption? PotionToAdd
    {
        get => _potionToAdd;
        set => SetProperty(ref _potionToAdd, value);
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

    public void AddRelicSelection()
    {
        if (RelicToAdd is null || HasOption(SelectedRelics, RelicToAdd.Id))
        {
            return;
        }

        SelectedRelics.Add(RelicToAdd);
    }

    public void RemoveRelicSelection(SelectableOption? option)
    {
        if (option == null)
        {
            return;
        }

        SelectedRelics.Remove(option);
    }

    public void AddCardSelection()
    {
        if (CardToAdd is null)
        {
            return;
        }

        SelectedCards.Add(CreateSelectionOption(CardToAdd));
    }

    public void RemoveCardSelection(SelectableOption? option)
    {
        if (option == null)
        {
            return;
        }

        SelectedCards.Remove(option);
    }

    public void AddPotionSelection()
    {
        if (PotionToAdd is null || HasOption(SelectedPotions, PotionToAdd.Id))
        {
            return;
        }

        SelectedPotions.Add(PotionToAdd);
    }

    public void RemovePotionSelection(SelectableOption? option)
    {
        if (option == null)
        {
            return;
        }

        SelectedPotions.Remove(option);
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
            if (summary.IsCanceled)
            {
                StatusMessage = "用户取消。";
                AppendLog(RollerLogLevel.Warning, $"已取消 roll，当前进度（{summary.ProcessedSeeds} 个种子，命中 {summary.MatchedSeeds}）已保存到：{summary.ResultJsonPath}");
            }
            else
            {
                StatusMessage = $"完成：共 {summary.ProcessedSeeds} 个种子，命中 {summary.MatchedSeeds}（{summary.MatchedOptions} 个选项）。";
                AppendLog(RollerLogLevel.Info, $"结果已保存到：{summary.ResultJsonPath}");
            }
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
            ReplaceSelections(_selectedRelics, filter.RelicIds, _availableRelics);
            ReplaceSelections(_selectedCards, filter.CardIds, _availableCards, allowDuplicates: true);
            ReplaceSelections(_selectedPotions, filter.PotionIds, _availablePotions);
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
            Kind = null,
            RelicTerms = Array.Empty<string>(),
            RelicIds = SelectedRelics.Select(option => option.Id).ToArray(),
            CardIds = SelectedCards.Select(option => option.Id).ToArray(),
            PotionIds = SelectedPotions.Select(option => option.Id).ToArray()
        };

        return new RollerConfig
        {
            GameDataPath = NormalizeOrNull(GameDataPath),
            StartSeed = NormalizeOrNull(StartSeed),
            Count = ParsePositiveIntOrNull(CountText),
            Mode = SelectedSeedMode switch
            {
                SeedMode.Incremental => "incremental",
                SeedMode.UntilMatch => "until-match",
                _ => "random"
            },
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
            ResultJsonPath = string.IsNullOrWhiteSpace(ResultJsonPath) ? SeedRollerDefaults.DefaultResultJson : ResultJsonPath.Trim()
        }.WithFilter(builder =>
        {
            builder.Kind = null;
            builder.RelicIds.AddRange(SelectedRelics.Select(option => option.Id));
            builder.CardIds.AddRange(SelectedCards.Select(option => option.Id));
            builder.PotionIds.AddRange(SelectedPotions.Select(option => option.Id));
        });
    }

    private void ReplaceSelections(ObservableCollection<SelectableOption> target, string[]? ids, ObservableCollection<SelectableOption> sourcePool, bool allowDuplicates = false)
    {
        target.Clear();
        if (ids == null)
        {
            return;
        }

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!allowDuplicates && HasOption(target, id))
            {
                continue;
            }

            var source = FindOptionOrCreate(sourcePool, id);
            target.Add(allowDuplicates ? CreateSelectionOption(source) : source);
        }
    }

    private static bool HasOption(IEnumerable<SelectableOption> source, string id)
    {
        return source.Any(option => string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static SelectableOption FindOptionOrCreate(IEnumerable<SelectableOption> source, string id)
    {
        var existing = source.FirstOrDefault(option => string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase));
        return existing ?? new SelectableOption(id, id);
    }

    private static SelectableOption CreateSelectionOption(SelectableOption template)
    {
        return new SelectableOption(template.Id, template.Display);
    }

    private static string FormatRelicLabel(SeedInfoOption option)
    {
        var title = string.IsNullOrWhiteSpace(option.Title) ? option.RelicId : option.Title!;
        var baseLabel = $"{title} ({option.RelicId})";
        return string.IsNullOrWhiteSpace(option.Note) ? baseLabel : $"{baseLabel} - {option.Note}";
    }

    private static string FormatEntryLabel(SeedInfoEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : $"{entry.Name} ({entry.Id})";
    }

    private bool RelicFilter(object? item) => MatchesSearch(item, _relicSearchText);

    private bool CardFilter(object? item) => MatchesSearch(item, _cardSearchText);

    private bool PotionFilter(object? item) => MatchesSearch(item, _potionSearchText);

    private static bool MatchesSearch(object? item, string searchText)
    {
        if (item is not SelectableOption option)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return option.Display.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || option.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase);
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
            "until-match" or "untilmatch" or "match" or "stoponmatch" => SeedMode.UntilMatch,
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

file static class RollerSettingsExtensions
{
    public static RollerSettings WithFilter(this RollerSettings settings, Action<RollerFilterSettings> apply)
    {
        apply(settings.Filter);
        return settings;
    }
}

public sealed class SelectableOption
{
    public SelectableOption(string id, string display)
    {
        Id = id;
        Display = display;
    }

    public string Id { get; }
    public string Display { get; }
}

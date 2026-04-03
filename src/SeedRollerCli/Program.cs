using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SeedRollerCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = CliOptions.Parse(args, SeedRollerDefaults.DefaultGameDataPath);
        try
        {
            var runner = new SeedRollerRunner();
            runner.Run(options);
            return 0;
        }
        catch (Exception ex)
        {
            RollerLog.Error($"种子工具：{ex}");
            return 1;
        }
    }
}

internal static class GameAssemblyResolver
{
    private static bool _registered;
    private static string? _gameDataPath;
    private static readonly string[] AdditionalSearchRoots =
    [
        string.Empty,
        "GodotSharp",
        Path.Combine("GodotSharp", "Api"),
        Path.Combine("GodotSharp", "Api", "Debug"),
        Path.Combine("GodotSharp", "Api", "Release"),
        Path.Combine("GodotSharp", "Tools")
    ];

    public static void Register(string gameDataPath)
    {
        if (_registered)
        {
            return;
        }

        if (!Directory.Exists(gameDataPath))
        {
            throw new DirectoryNotFoundException($"找不到游戏数据目录: {gameDataPath}");
        }

        _gameDataPath = gameDataPath;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromGameData;
        _registered = true;
    }

    private static Assembly? ResolveFromGameData(object? sender, ResolveEventArgs args)
    {
        if (_gameDataPath == null)
        {
            return null;
        }

        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var candidate in EnumerateCandidates(name))
        {
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(_gameDataPath))
        {
            yield break;
        }

        foreach (var subdir in AdditionalSearchRoots)
        {
            var baseDir = string.IsNullOrEmpty(subdir)
                ? _gameDataPath
                : Path.Combine(_gameDataPath, subdir);
            yield return Path.Combine(baseDir, assemblyName + ".dll");
        }
    }
}

internal static class GameBootstrapper
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        LocalizationPatchManager.EnsurePatched();

        TestMode.TurnOnInternal();

        ModelDb.Init();
        ModelIdSerializationCache.Init();
        ModelDb.InitIds();

        _initialized = true;
    }
}

internal sealed class CliOptions
{
    private readonly CharacterChoice _characterChoice;
    private readonly string _rawSeed;
    private string? _normalizedSeed;

    internal CliOptions(string gameDataPath, int count, string rawSeed, SeedMode mode, CharacterChoice characterChoice, int ascension, string? seedInfoPath, OptionFilter filter, string resultJsonPath)
    {
        GameDataPath = gameDataPath;
        Count = count;
        _rawSeed = rawSeed;
        Mode = mode;
        _characterChoice = characterChoice;
        Ascension = ascension;
        SeedInfoPath = string.IsNullOrWhiteSpace(seedInfoPath) ? null : seedInfoPath;
        Filter = filter;
        ResultJsonPath = string.IsNullOrWhiteSpace(resultJsonPath)
            ? Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultResultJson)
            : resultJsonPath;
    }

    public string GameDataPath { get; }

    public int Count { get; }

    public string StartSeed => _normalizedSeed ??= SeedFormatter.Normalize(_rawSeed);

    public SeedMode Mode { get; }

    public int Ascension { get; }

    public string? SeedInfoPath { get; }

    public OptionFilter Filter { get; }

    public string ResultJsonPath { get; }

    public CharacterModel ResolveCharacter()
    {
        return _characterChoice switch
        {
            CharacterChoice.Silent => ModelDb.Character<Silent>(),
            CharacterChoice.Regent => ModelDb.Character<Regent>(),
            CharacterChoice.Necrobinder => ModelDb.Character<Necrobinder>(),
            CharacterChoice.Defect => ModelDb.Character<Defect>(),
            _ => ModelDb.Character<Ironclad>()
        };
    }

    public static CliOptions Parse(string[] args, string defaultDataPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            string value;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            else
            {
                throw new ArgumentException($"参数 {arg} 缺少取值");
            }

            map[key] = value;
        }

        string? Arg(string key) => map.TryGetValue(key, out var value) ? value : null;

        RollerConfig? config = null;
        var configPath = Arg("config");
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            config = ConfigLoader.Load(configPath!);
        }

        string dataPath = FirstNonEmpty(
            Arg("game-data-path"),
            config?.GameDataPath,
            Environment.GetEnvironmentVariable("STS2_DATA_PATH"),
            defaultDataPath)!;

        int count = TryParsePositiveInt(Arg("count"))
            ?? config?.Count
            ?? 10;
        if (count <= 0)
        {
            count = 10;
        }

        string seed = FirstNonEmpty(Arg("start-seed"), config?.StartSeed, SeedRollerDefaults.DefaultSeed)!;

        var modeText = FirstNonEmpty(Arg("mode"), config?.Mode);
        var mode = ParseSeedMode(modeText);

        var characterText = FirstNonEmpty(Arg("character"), config?.Character);
        var character = ParseCharacter(characterText);

        int ascension = TryParseNonNegativeInt(Arg("ascension"))
            ?? config?.Ascension
            ?? 0;
        if (ascension < 0)
        {
            ascension = 0;
        }

        var filterKindText = FirstNonEmpty(Arg("filter-kind"), config?.Filter?.Kind);
        var filterKind = string.IsNullOrWhiteSpace(filterKindText) ? null : ParseFilterKind(filterKindText);

        var relicTerms = ParseFilterTerms(Arg("filter-relic"))
            .Concat(config?.Filter?.RelicTerms ?? Array.Empty<string>());
        var relicIds = ParseFilterTerms(Arg("filter-relic-id"))
            .Concat(config?.Filter?.RelicIds ?? Array.Empty<string>());
        var cardIds = ParseFilterTerms(Arg("filter-card-id"))
            .Concat(config?.Filter?.CardIds ?? Array.Empty<string>());
        var potionIds = ParseFilterTerms(Arg("filter-potion-id"))
            .Concat(config?.Filter?.PotionIds ?? Array.Empty<string>());

        var filter = OptionFilter.Create(filterKind, relicTerms, relicIds, cardIds, potionIds);
        var seedInfoPath = FirstNonEmpty(Arg("seed-info"), config?.SeedInfoPath);
        var resultJson = FirstNonEmpty(Arg("result-json"), config?.ResultJson, SeedRollerDefaults.DefaultResultJson);

        return new CliOptions(dataPath, count, seed, mode, character, ascension, seedInfoPath, filter, resultJson!);
    }

    private static readonly char[] FilterSeparators = new[] { ',', ';', '|' };

    private static string[] ParseFilterTerms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(FilterSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0)
            .ToArray();
    }

    private static NeowChoiceKind? ParseFilterKind(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "positive" => NeowChoiceKind.Positive,
            "pos" => NeowChoiceKind.Positive,
            "p" => NeowChoiceKind.Positive,
            "curse" => NeowChoiceKind.Curse,
            "c" => NeowChoiceKind.Curse,
            "negative" => NeowChoiceKind.Curse,
            "cost" => NeowChoiceKind.Curse,
            "all" => null,
            "any" => null,
            _ => LogUnknownFilterKind(value)
        };
    }

    private static NeowChoiceKind? LogUnknownFilterKind(string value)
    {
        RollerLog.Warning($"未识别的筛选类型：{value}");
        return null;
    }

    private static SeedMode ParseSeedMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SeedMode.Random;
        }

        return value?.ToLowerInvariant() switch
        {
            "incremental" or "inc" or "sequential" => SeedMode.Incremental,
            "until-match" or "untilmatch" or "match" or "stoponmatch" => SeedMode.UntilMatch,
            "random" or "rand" => SeedMode.Random,
            _ => SeedMode.Random
        };
    }

    private static CharacterChoice ParseCharacter(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "silent" => CharacterChoice.Silent,
            "regent" => CharacterChoice.Regent,
            "necrobinder" => CharacterChoice.Necrobinder,
            "defect" => CharacterChoice.Defect,
            _ => CharacterChoice.Ironclad
        };
    }

    private static int? TryParsePositiveInt(string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static int? TryParseNonNegativeInt(string? value)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

public enum CharacterChoice
{
    Ironclad,
    Silent,
    Regent,
    Necrobinder,
    Defect
}

public enum SeedMode
{
    Incremental,
    Random,
    UntilMatch
}

public sealed class RollerConfig
{
    public string? GameDataPath { get; set; }
    public string? SeedInfoPath { get; set; }
    public string? StartSeed { get; set; }
    public int? Count { get; set; }
    public string? Mode { get; set; }
    public string? Character { get; set; }
    public int? Ascension { get; set; }
    public RollerFilterConfig? Filter { get; set; }
    public string? ResultJson { get; set; }
}

public sealed class RollerFilterConfig
{
    public string? Kind { get; set; }
    public string[]? RelicTerms { get; set; }
    public string[]? RelicIds { get; set; }
    public string[]? CardIds { get; set; }
    public string[]? PotionIds { get; set; }
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RollerConfig Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"找不到配置文件: {fullPath}", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        var config = JsonSerializer.Deserialize<RollerConfig>(stream, Options);
        if (config == null)
        {
            throw new InvalidOperationException($"无法解析配置: {fullPath}");
        }

        return config;
    }
}

internal static class SeedSequence
{
    public static IEnumerable<string> Generate(CliOptions options)
    {
        if (options.Mode == SeedMode.Random)
        {
            while (true)
            {
                yield return SeedHelper.GetRandomSeed();
            }
        }

        string current = options.StartSeed;
        while (true)
        {
            yield return current;
            current = SeedMath.Increment(current);
        }
    }
}

internal static class SeedFormatter
{
    public static string Normalize(string seed)
    {
        seed = SeedHelper.CanonicalizeSeed(seed);
        if (seed.Length < SeedHelper.seedDefaultLength)
        {
            seed = seed.PadRight(SeedHelper.seedDefaultLength, '0');
        }
        else if (seed.Length > SeedHelper.seedDefaultLength)
        {
            seed = seed[..SeedHelper.seedDefaultLength];
        }
        SeedMath.Validate(seed);
        return seed;
    }
}

internal static class SeedMath
{
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Increment(string seed)
    {
        var chars = seed.ToCharArray();
        for (int i = chars.Length - 1; i >= 0; i--)
        {
            int index = Alphabet.IndexOf(chars[i]);
            if (index < 0)
            {
                throw new ArgumentException($"非法种子字符: {chars[i]}");
            }
            index++;
            if (index < Alphabet.Length)
            {
                chars[i] = Alphabet[index];
                for (int j = i + 1; j < chars.Length; j++)
                {
                    chars[j] = Alphabet[0];
                }
                return new string(chars);
            }
            chars[i] = Alphabet[0];
        }
        return new string(chars);
    }

    public static void Validate(string seed)
    {
        foreach (char c in seed)
        {
            if (!Alphabet.Contains(c))
            {
                throw new ArgumentException($"种子包含非法字符: {c}");
            }
        }
    }
}


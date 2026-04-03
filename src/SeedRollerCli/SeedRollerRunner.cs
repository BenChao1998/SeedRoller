using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SeedRollerCli;

public sealed record RollerProgress(int ProcessedSeeds, int MatchedSeeds, int MatchedOptions);

public sealed record RollerRunSummary(int ProcessedSeeds, int MatchedSeeds, int MatchedOptions, string ResultJsonPath, bool IsCanceled);

public sealed class RollerFilterSettings
{
    public NeowChoiceKind? Kind { get; set; }
    public List<string> RelicTerms { get; } = new();
    public List<string> RelicIds { get; } = new();
    public List<string> CardIds { get; } = new();
    public List<string> PotionIds { get; } = new();
}

public sealed class RollerSettings
{
    public string GameDataPath { get; set; } = SeedRollerDefaults.DefaultGameDataPath;
    public int Count { get; set; } = 10;
    public string StartSeed { get; set; } = SeedRollerDefaults.DefaultSeed;
    public SeedMode Mode { get; set; } = SeedMode.Random;
    public CharacterChoice Character { get; set; } = CharacterChoice.Ironclad;
    public int Ascension { get; set; }
    public string? SeedInfoPath { get; set; }
    public RollerFilterSettings Filter { get; } = new();
    public string ResultJsonPath { get; set; } = SeedRollerDefaults.DefaultResultJson;
}

public sealed class SeedRollerRunner
{
    public RollerRunSummary Run(RollerSettings settings, IProgress<RollerProgress>? progress = null, IRollerLogSink? logSink = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var scope = RollerLog.PushLogger(logSink);
        try
        {
            var options = BuildCliOptions(settings);
            return Run(options, progress, cancellationToken);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    internal RollerRunSummary Run(CliOptions options, IProgress<RollerProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        Initialize(options);
        return RunCore(options, progress, cancellationToken);
    }

    private static RollerRunSummary RunCore(CliOptions options, IProgress<RollerProgress>? progress, CancellationToken cancellationToken)
    {
        var resultWriter = new RollResultWriter(options.Filter, options.ResultJsonPath);
        var roller = new NeowSeedRoller(options.ResolveCharacter(), options.Ascension);
        var seeds = SeedSequence.Generate(options);
        var canceled = false;
        var processed = 0;
        foreach (var seed in seeds)
        {
            if (ShouldStopDueToCount(options, processed))
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var result = roller.Roll(seed);
            processed++;
            var matched = resultWriter.Process(result);
            progress?.Report(new RollerProgress(
                resultWriter.TotalSeeds,
                resultWriter.MatchedSeedCount,
                resultWriter.MatchedOptionCount));

            if (options.Mode == SeedMode.UntilMatch && matched)
            {
                break;
            }
        }

        resultWriter.Complete(canceled);
        return new RollerRunSummary(
            resultWriter.TotalSeeds,
            resultWriter.MatchedSeedCount,
            resultWriter.MatchedOptionCount,
            Path.GetFullPath(resultWriter.OutputPath),
            canceled);
    }

    private static bool ShouldStopDueToCount(CliOptions options, int processedSeeds)
    {
        if (options.Mode == SeedMode.UntilMatch)
        {
            return false;
        }

        return processedSeeds >= options.Count;
    }

    private static void Initialize(CliOptions options)
    {
        GameAssemblyResolver.Register(options.GameDataPath);
        GameBootstrapper.Initialize();
        LocalizationProvider.Initialize(options.SeedInfoPath);
    }

    private static CliOptions BuildCliOptions(RollerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GameDataPath))
        {
            throw new ArgumentException("必须提供游戏 data 路径", nameof(settings));
        }

        var normalizedCount = settings.Count <= 0 ? 10 : settings.Count;
        var normalizedAscension = settings.Ascension < 0 ? 0 : settings.Ascension;
        var normalizedSeed = string.IsNullOrWhiteSpace(settings.StartSeed) ? SeedRollerDefaults.DefaultSeed : settings.StartSeed!;
        var normalizedResultJson = string.IsNullOrWhiteSpace(settings.ResultJsonPath) ? SeedRollerDefaults.DefaultResultJson : settings.ResultJsonPath!;
        var normalizedSeedInfo = string.IsNullOrWhiteSpace(settings.SeedInfoPath) ? null : settings.SeedInfoPath!;

        var filter = OptionFilter.Create(
            settings.Filter.Kind,
            settings.Filter.RelicTerms,
            settings.Filter.RelicIds,
            settings.Filter.CardIds,
            settings.Filter.PotionIds);

        return new CliOptions(
            settings.GameDataPath,
            normalizedCount,
            normalizedSeed,
            settings.Mode,
            settings.Character,
            normalizedAscension,
            normalizedSeedInfo,
            filter,
            normalizedResultJson);
    }
}

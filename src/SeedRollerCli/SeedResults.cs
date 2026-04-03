using System;
using System.Collections.Generic;

namespace SeedRollerCli;

internal sealed record SeedRollResult(string Seed, IReadOnlyList<NeowOptionResult> Options);

internal sealed record SerializableOption(
    string Kind,
    string RelicId,
    string Title,
    string Description,
    string? Note,
    IReadOnlyList<RewardDetail> Details);

internal sealed record SerializableSeedResult(string Seed, IReadOnlyList<SerializableOption> Options);

internal sealed record RollResults(
    DateTimeOffset GeneratedAt,
    int TotalSeeds,
    int MatchedSeeds,
    int MatchedOptions,
    IReadOnlyList<SerializableSeedResult> Seeds);

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SeedRollerCli;

public enum NeowChoiceKind
{
    Positive,
    Curse
}

internal readonly record struct RunContext(RunState State, Player Player);

internal readonly record struct NeowOptionResult(
    NeowChoiceKind Kind,
    ModelId ModelId,
    string DisplayName,
    string? Note,
    Type RelicType,
    IReadOnlyList<RewardDetail> Details);

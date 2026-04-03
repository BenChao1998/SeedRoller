using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SeedRollerCli;

internal sealed class NeowSeedRoller
{
    private readonly CharacterModel _character;
    private readonly int _ascension;
    private readonly IReadOnlyList<ModifierModel> _modifiers;

    public NeowSeedRoller(CharacterModel character, int ascension)
    {
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _ascension = ascension;
        _modifiers = Array.Empty<ModifierModel>();
    }

    public SeedRollResult Roll(string seed)
    {
        var normalized = SeedFormatter.Normalize(seed);
        var context = CreateRunContext(normalized);
        var rng = NeowOptionLogic.CreateRng(context.State, context.Player);
        var contextFactory = new Func<RunContext>(() => CreateRunContext(normalized));

        var enriched = NeowOptionLogic.Generate(context.State, context.Player, rng)
            .Select(option => option with
            {
                Details = RewardPreviewProvider.GetDetails(option, contextFactory)
            })
            .ToList();

        return new SeedRollResult(normalized, enriched);
    }

    private RunContext CreateRunContext(string seed)
    {
        var player = Player.CreateForNewRun(_character, UnlockState.all, 1);
        var acts = ActModel.GetDefaultList()
            .Select(static act => act.ToMutable())
            .ToList();
        var modifiers = _modifiers
            .Select(static modifier => modifier.ToMutable())
            .ToList();
        var runPlayers = new List<Player> { player };
        var state = RunState.CreateForNewRun(runPlayers, acts, modifiers, _ascension, seed);
        return new RunContext(state, player);
    }
}

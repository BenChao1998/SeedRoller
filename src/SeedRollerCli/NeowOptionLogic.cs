using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace SeedRollerCli;

internal static class NeowOptionLogic
{
    private sealed record OptionDef(Type RelicType, NeowChoiceKind Kind, string? Note = null);

    private static readonly string NeowIdEntry;
    private static readonly ulong NeowHash;
    private static readonly OptionDef[] PositiveCore;
    private static readonly OptionDef[] CurseCore;
    private static readonly OptionDef ToughnessOption;
    private static readonly OptionDef SafetyOption;
    private static readonly OptionDef ClericOption;
    private static readonly OptionDef PatienceOption;
    private static readonly OptionDef ScavengerOption;
    private static readonly OptionDef BundleOption;
    private static readonly OptionDef EmpowerOption;

    static NeowOptionLogic()
    {
        NeowIdEntry = ModelDb.Event<Neow>().Id.Entry;
        NeowHash = (ulong)StringHelper.GetDeterministicHashCode(NeowIdEntry);

        PositiveCore =
        [
            new OptionDef(typeof(ArcaneScroll), NeowChoiceKind.Positive),
            new OptionDef(typeof(BoomingConch), NeowChoiceKind.Positive),
            new OptionDef(typeof(Pomander), NeowChoiceKind.Positive),
            new OptionDef(typeof(GoldenPearl), NeowChoiceKind.Positive),
            new OptionDef(typeof(LeadPaperweight), NeowChoiceKind.Positive),
            new OptionDef(typeof(NewLeaf), NeowChoiceKind.Positive),
            new OptionDef(typeof(NeowsTorment), NeowChoiceKind.Positive),
            new OptionDef(typeof(PreciseScissors), NeowChoiceKind.Positive),
            new OptionDef(typeof(LostCoffer), NeowChoiceKind.Positive)
        ];

        ToughnessOption = new OptionDef(typeof(NutritiousOyster), NeowChoiceKind.Positive, "韧性");
        SafetyOption = new OptionDef(typeof(StoneHumidifier), NeowChoiceKind.Positive, "安全");
        ClericOption = new OptionDef(typeof(MassiveScroll), NeowChoiceKind.Positive, "牧师");
        PatienceOption = new OptionDef(typeof(LavaRock), NeowChoiceKind.Positive, "耐心");
        ScavengerOption = new OptionDef(typeof(SmallCapsule), NeowChoiceKind.Positive, "拾荒");

        CurseCore =
        [
            new OptionDef(typeof(CursedPearl), NeowChoiceKind.Curse),
            new OptionDef(typeof(LargeCapsule), NeowChoiceKind.Curse),
            new OptionDef(typeof(LeafyPoultice), NeowChoiceKind.Curse),
            new OptionDef(typeof(PrecariousShears), NeowChoiceKind.Curse)
        ];

        BundleOption = new OptionDef(typeof(ScrollBoxes), NeowChoiceKind.Curse, "礼盒");
        EmpowerOption = new OptionDef(typeof(SilverCrucible), NeowChoiceKind.Curse, "灌能");
    }

    public static Rng CreateRng(RunState state, Player player)
    {
        var seed = (uint)(state.Rng.Seed + player.NetId + NeowHash);
        return new Rng(seed, 0);
    }

    public static IReadOnlyList<NeowOptionResult> Generate(RunState state, Player player, Rng rng)
    {
        if (state.Modifiers.Count > 0)
        {
            throw new NotSupportedException("暂不支持带模组的开局奖励计算");
        }

        var cursePool = BuildCursePool(player).ToList();
        var curse = rng.NextItem(cursePool);
        if (curse is null)
        {
            throw new InvalidOperationException("诅咒选项为空");
        }

        var positivePool = BuildPositivePool(state, player, curse, rng).ToList();
        var shuffled = ListExtensions.UnstableShuffle(positivePool, rng);
        var selected = shuffled.Take(2).ToList();
        selected.Add(curse);

        return selected
            .Select(ToResult)
            .ToList();
    }

    public static IReadOnlyList<NeowOptionInfo> DescribeAllOptions()
    {
        var defs = new List<OptionDef>();
        defs.AddRange(PositiveCore);
        defs.AddRange(CurseCore);
        defs.AddRange(new[]
        {
            ToughnessOption,
            SafetyOption,
            ClericOption,
            PatienceOption,
            ScavengerOption,
            BundleOption,
            EmpowerOption
        });

        return defs
            .GroupBy(d => d.RelicType)
            .Select(group =>
            {
                var def = group.First();
                var modelId = ModelDb.GetId(def.RelicType);
                var fallback = StringHelper.Unslugify(modelId.Entry);
                var (title, description) = LocalizationProvider.GetRelicStrings(modelId, fallback);
                return new NeowOptionInfo(modelId, def.Kind, title, description, def.Note);
            })
            .OrderBy(info => info.Kind)
            .ThenBy(info => info.Title, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<OptionDef> BuildCursePool(Player player)
    {
        var list = new List<OptionDef>(CurseCore);
        if (ScrollBoxes.CanGenerateBundles(player))
        {
            list.Add(BundleOption);
        }

        var players = player.RunState?.Players;
        if (players is { Count: 1 })
        {
            list.Add(EmpowerOption);
        }

        return list;
    }

    private static IEnumerable<OptionDef> BuildPositivePool(RunState state, Player player, OptionDef curse, Rng rng)
    {
        var list = new List<OptionDef>(PositiveCore);

        if (curse.RelicType == typeof(CursedPearl))
        {
            list.RemoveAll(o => o.RelicType == typeof(GoldenPearl));
        }

        if (curse.RelicType == typeof(PrecariousShears))
        {
            list.RemoveAll(o => o.RelicType == typeof(PreciseScissors));
        }

        if (curse.RelicType == typeof(LeafyPoultice))
        {
            list.RemoveAll(o => o.RelicType == typeof(NewLeaf));
        }

        if (state.Players.Count > 1)
        {
            list.Add(ClericOption);
        }

        list.Add(rng.NextBool() ? ToughnessOption : SafetyOption);

        if (curse.RelicType != typeof(LargeCapsule))
        {
            list.Add(rng.NextBool() ? PatienceOption : ScavengerOption);
        }

        return list;
    }

    private static NeowOptionResult ToResult(OptionDef def)
    {
        var modelId = ModelDb.GetId(def.RelicType);
        var display = StringHelper.Unslugify(modelId.Entry);
        return new NeowOptionResult(
            def.Kind,
            modelId,
            display,
            def.Note,
            def.RelicType,
            Array.Empty<RewardDetail>());
    }
}

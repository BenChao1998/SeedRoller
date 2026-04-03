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
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
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

        string[] candidates =
        [
            Path.Combine(_gameDataPath, name + ".dll"),
            Path.Combine(_gameDataPath, "GodotSharp", name + ".dll")
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
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
        var saveManager = new SaveManager(new MockGodotFileIo("user://seedroller"), forceSynchronous: true);
        SaveManager.MockInstanceForTesting(saveManager);
        saveManager.InitSettingsDataForTest();
        saveManager.InitPrefsDataForTest();
        saveManager.Progress = new ProgressState();

        ModelDb.Init();
        ModelIdSerializationCache.Init();
        ModelDb.InitIds();

        saveManager.Progress = ProgressState.CreateDefault();

        _initialized = true;
    }
}

internal static class LocalizationPatchManager
{
    private static bool _patched;
    private static Harmony? _harmony;

    public static void EnsurePatched()
    {
        if (_patched)
        {
            return;
        }

        _harmony = new Harmony("seedroller.localization");
        _harmony.PatchAll(typeof(LocalizationPatchManager).Assembly);
        LocalizationStub.Initialize();
        _patched = true;
    }

    private static class LocalizationStub
    {
        public static void Initialize()
        {
            var locType = typeof(LocManager);
            if (locType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) != null)
            {
                return;
            }

            var instance = FormatterServices.GetUninitializedObject(locType);
            SetField(instance, "_tables", new Dictionary<string, LocTable>(StringComparer.OrdinalIgnoreCase));
            SetField(instance, "_engTables", new Dictionary<string, LocTable>(StringComparer.OrdinalIgnoreCase));
            SetField(instance, "_stateBeforeOverridingWithEnglish", null);
            SetField(instance, "_languageKeyCount", new Dictionary<string, int>());
            SetField(instance, "_localeChangeCallbacks", new List<LocManager.LocaleChangeCallback>());

            SetProperty(instance, "OverridesActive", false);
            SetProperty(instance, "ValidationErrors", Array.Empty<LocValidationError>());
            SetProperty(instance, "Language", "eng");
            SetProperty(instance, "CultureInfo", CultureInfo.GetCultureInfo("en-US"));

            locType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!
                .SetValue(null, instance);
        }

        private static void SetField(object target, string name, object? value)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        private static void SetProperty(object target, string name, object? value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(target, value);
        }
    }

    [HarmonyPatch(typeof(LocManager), nameof(LocManager.GetTable))]
    private static class LocManagerGetTablePatch
    {
        private static readonly FieldInfo TablesField = typeof(LocManager).GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)!;

        static bool Prefix(LocManager __instance, string name, ref LocTable __result)
        {
            var tables = (Dictionary<string, LocTable>)TablesField.GetValue(__instance)!;
            if (!tables.TryGetValue(name, out var table))
            {
                table = new LocTable(name, new Dictionary<string, string>());
                tables[name] = table;
            }

            __result = table;
            return false;
        }
    }

    [HarmonyPatch(typeof(LocString), nameof(LocString.GetFormattedText))]
    private static class LocStringGetFormattedTextPatch
    {
        static bool Prefix(LocString __instance, ref string __result)
        {
            __result = __instance.GetRawText();
            return false;
        }
    }

    [HarmonyPatch(typeof(LocTable), nameof(LocTable.GetRawText))]
    private static class LocTableGetRawTextPatch
    {
        private static readonly FieldInfo TranslationsField = typeof(LocTable).GetField("_translations", BindingFlags.NonPublic | BindingFlags.Instance)!;

        static bool Prefix(LocTable __instance, string key, ref string __result)
        {
            var translations = (Dictionary<string, string>)TranslationsField.GetValue(__instance)!;
            if (translations.TryGetValue(key, out var value))
            {
                __result = value;
                return false;
            }

            __result = key;
            return false;
        }
    }

    [HarmonyPatch(typeof(LocTable), nameof(LocTable.HasEntry))]
    private static class LocTableHasEntryPatch
    {
        static bool Prefix(ref bool __result)
        {
            __result = true;
            return false;
        }
    }
}

internal static class SaveManagerStub
{
    private static bool _installed;
    private static ProgressState _progress = new ProgressState();
    private static readonly SettingsSave _settings = new SettingsSave();
    private static readonly PrefsSave _prefs = new PrefsSave();

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        var fake = (SaveManager)FormatterServices.GetUninitializedObject(typeof(SaveManager));
        SaveManager.MockInstanceForTesting(fake);
        _installed = true;
    }

    public static ProgressState Progress
    {
        get => _progress;
        set => _progress = value;
    }

    public static SettingsSave Settings => _settings;

    public static PrefsSave Prefs => _prefs;
}

internal static class SaveManagerPatches
{
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Progress), MethodType.Getter)]
    private static class SaveManagerProgressGetterPatch
    {
        static bool Prefix(ref ProgressState __result)
        {
            __result = SaveManagerStub.Progress;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Progress), MethodType.Setter)]
    private static class SaveManagerProgressSetterPatch
    {
        static bool Prefix(ProgressState value)
        {
            SaveManagerStub.Progress = value;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SettingsSave), MethodType.Getter)]
    private static class SaveManagerSettingsGetterPatch
    {
        static bool Prefix(ref SettingsSave __result)
        {
            __result = SaveManagerStub.Settings;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.PrefsSave), MethodType.Getter)]
    private static class SaveManagerPrefsGetterPatch
    {
        static bool Prefix(ref PrefsSave __result)
        {
            __result = SaveManagerStub.Prefs;
            return false;
        }
    }
}

[HarmonyPatch(typeof(Logger), "GetIsRunningFromGodotEditor")]
internal static class LoggerGetIsRunningFromGodotEditorPatch
{
    static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ConsoleLogPrinter), nameof(ConsoleLogPrinter.Print))]
internal static class ConsoleLogPrinterPrintPatch
{
    static bool Prefix(LogLevel logLevel, string text, int skipFrames)
    {
        var label = logLevel switch
        {
            LogLevel.Debug => "调试",
            LogLevel.Info => "信息",
            LogLevel.Warn => "警告",
            LogLevel.Error => "错误",
            _ => logLevel.ToString()
        };
        RollerLog.Info($"[{label}] {text}");
        return false;
    }
}

internal sealed class NeowSeedRoller
{
    private readonly CharacterModel _character;
    private readonly int _ascension;
    private readonly IReadOnlyList<ModifierModel> _modifiers = Array.Empty<ModifierModel>();

    public NeowSeedRoller(CharacterModel character, int ascension)
    {
        _character = character;
        _ascension = ascension;
    }

    public SeedRollResult Roll(string seed)
    {
        var normalized = SeedFormatter.Normalize(seed);
        var context = CreateRunContext(normalized);
        var rng = NeowOptionLogic.CreateRng(context.State, context.Player);
        var baseOptions = NeowOptionLogic.Generate(context.State, context.Player, rng);
        var contextFactory = new Func<RunContext>(() => CreateRunContext(normalized));
        var enriched = baseOptions
            .Select(option => option with { Details = RewardPreviewProvider.GetDetails(option, contextFactory) })
            .ToList();
        return new SeedRollResult(normalized, enriched);
    }

    private RunContext CreateRunContext(string seed)
    {
        var player = Player.CreateForNewRun(_character, UnlockState.all, 1uL);
        var acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
        var modifiers = _modifiers.Select(m => m.ToMutable()).ToList();
        var state = RunState.CreateForNewRun(new List<Player> { player }, acts, modifiers, _ascension, seed);
        return new RunContext(state, player);
    }
}

internal readonly record struct RunContext(RunState State, Player Player);

internal static class NeowOptionLogic
{
    private static readonly string NeowIdEntry = ModelDb.Event<Neow>().Id.Entry;
    private static readonly ulong NeowHash = unchecked((ulong)StringHelper.GetDeterministicHashCode(NeowIdEntry));

    private static readonly OptionDef[] PositiveCore =
    {
        new(typeof(ArcaneScroll), NeowChoiceKind.Positive),
        new(typeof(BoomingConch), NeowChoiceKind.Positive),
        new(typeof(Pomander), NeowChoiceKind.Positive),
        new(typeof(GoldenPearl), NeowChoiceKind.Positive),
        new(typeof(LeadPaperweight), NeowChoiceKind.Positive),
        new(typeof(NewLeaf), NeowChoiceKind.Positive),
        new(typeof(NeowsTorment), NeowChoiceKind.Positive),
        new(typeof(PreciseScissors), NeowChoiceKind.Positive),
        new(typeof(LostCoffer), NeowChoiceKind.Positive)
    };

    private static readonly OptionDef ToughnessOption = new(typeof(NutritiousOyster), NeowChoiceKind.Positive, "韧性");
    private static readonly OptionDef SafetyOption = new(typeof(StoneHumidifier), NeowChoiceKind.Positive, "安全");
    private static readonly OptionDef ClericOption = new(typeof(MassiveScroll), NeowChoiceKind.Positive, "牧师");
    private static readonly OptionDef PatienceOption = new(typeof(LavaRock), NeowChoiceKind.Positive, "耐心");
    private static readonly OptionDef ScavengerOption = new(typeof(SmallCapsule), NeowChoiceKind.Positive, "拾荒");

    private static readonly OptionDef[] CurseCore =
    {
        new(typeof(CursedPearl), NeowChoiceKind.Curse),
        new(typeof(LargeCapsule), NeowChoiceKind.Curse),
        new(typeof(LeafyPoultice), NeowChoiceKind.Curse),
        new(typeof(PrecariousShears), NeowChoiceKind.Curse)
    };

    private static readonly OptionDef BundleOption = new(typeof(ScrollBoxes), NeowChoiceKind.Curse, "礼盒");
    private static readonly OptionDef EmpowerOption = new(typeof(SilverCrucible), NeowChoiceKind.Curse, "灌能");

    public static Rng CreateRng(RunState state, Player player)
    {
        ulong combined = state.Rng.Seed + player.NetId + NeowHash;
        return new Rng((uint)combined);
    }

    public static IReadOnlyList<NeowOptionResult> Generate(RunState state, Player player, Rng rng)
    {
        if (state.Modifiers.Count > 0)
        {
            throw new NotSupportedException("暂不支持带模组的开局奖励计算");
        }

        var curseChoices = BuildCursePool(player).ToList();
        var curse = rng.NextItem(curseChoices);
        if (curse == null)
        {
            throw new InvalidOperationException("诅咒选项为空");
        }

        var positivePool = BuildPositivePool(state, player, curse, rng);
        var picks = positivePool.ToList().UnstableShuffle(rng).Take(2).ToList();
        picks.Add(curse);

        return picks.Select(ToResult).ToList();
    }

    private static IEnumerable<OptionDef> BuildCursePool(Player player)
    {
        var pool = new List<OptionDef>(CurseCore);
        if (ScrollBoxes.CanGenerateBundles(player))
        {
            pool.Add(BundleOption);
        }
        if (player.RunState.Players.Count == 1)
        {
            pool.Add(EmpowerOption);
        }
        return pool;
    }

    private static IEnumerable<OptionDef> BuildPositivePool(RunState state, Player player, OptionDef curse, Rng rng)
    {
        var pool = new List<OptionDef>(PositiveCore);
        if (curse.RelicType == typeof(CursedPearl))
        {
            pool.RemoveAll(o => o.RelicType == typeof(GoldenPearl));
        }
        if (curse.RelicType == typeof(PrecariousShears))
        {
            pool.RemoveAll(o => o.RelicType == typeof(PreciseScissors));
        }
        if (curse.RelicType == typeof(LeafyPoultice))
        {
            pool.RemoveAll(o => o.RelicType == typeof(NewLeaf));
        }
        if (state.Players.Count > 1)
        {
            pool.Add(ClericOption);
        }
        pool.Add(rng.NextBool() ? ToughnessOption : SafetyOption);
        if (curse.RelicType != typeof(LargeCapsule))
        {
            pool.Add(rng.NextBool() ? PatienceOption : ScavengerOption);
        }
        return pool;
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
            .GroupBy(static d => d.RelicType)
            .Select(group =>
            {
                var def = group.First();
                var modelId = ModelDb.GetId(def.RelicType);
                var (title, description) = LocalizationProvider.GetRelicStrings(modelId, StringHelper.Unslugify(modelId.Entry));
                return new NeowOptionInfo(modelId, def.Kind, title, description, def.Note);
            })
            .OrderBy(static info => info.Kind)
            .ThenBy(static info => info.Title, StringComparer.Ordinal)
            .ToList();
    }

    private static NeowOptionResult ToResult(OptionDef def)
    {
        var modelId = ModelDb.GetId(def.RelicType);
        string display = StringHelper.Unslugify(modelId.Entry);
        return new NeowOptionResult(def.Kind, modelId, display, def.Note, def.RelicType, Array.Empty<RewardDetail>());
    }

    private sealed record OptionDef(Type RelicType, NeowChoiceKind Kind, string? Note = null);
}

internal sealed record NeowOptionInfo(ModelId ModelId, NeowChoiceKind Kind, string Title, string Description, string? Note);

internal static class RewardPreviewProvider
{
    public static IReadOnlyList<RewardDetail> GetDetails(NeowOptionResult option, Func<RunContext> contextFactory)
    {
        try
        {
            switch (option.RelicType)
            {
                case Type type when type == typeof(ArcaneScroll):
                    return PreviewArcaneScroll(contextFactory);
                case Type type when type == typeof(GoldenPearl):
                    return PreviewGoldenPearl(option);
                case Type type when type == typeof(CursedPearl):
                    return PreviewCursedPearl(option);
                case Type type when type == typeof(LostCoffer):
                    return PreviewLostCoffer(contextFactory);
                case Type type when type == typeof(LeadPaperweight):
                    return PreviewLeadPaperweight(contextFactory);
                case Type type when type == typeof(MassiveScroll):
                    return PreviewMassiveScroll(contextFactory);
                case Type type when type == typeof(NeowsTorment):
                    return PreviewNeowsTorment(contextFactory);
                case Type type when type == typeof(LargeCapsule):
                    return PreviewLargeCapsule(contextFactory);
                case Type type when type == typeof(LeafyPoultice):
                    return PreviewLeafyPoultice(contextFactory);
                case Type type when type == typeof(ScrollBoxes):
                    return PreviewScrollBoxes(contextFactory);
                default:
                    return Array.Empty<RewardDetail>();
            }
        }
        catch (Exception exception)
        {
            RollerLog.Warning($"奖励预览失败（{option.ModelId.Entry}）：{exception.Message}");
            return Array.Empty<RewardDetail>();
        }
    }

    private static IReadOnlyList<RewardDetail> PreviewArcaneScroll(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var options = new CardCreationOptions(
            new[] { player.Character.CardPool },
            CardCreationSource.Other,
            CardRarityOddsType.Uniform,
            static c => c.Rarity == CardRarity.Rare)
            .WithFlags(CardCreationFlags.NoUpgradeRoll);

        var card = CardFactory.CreateForReward(player, 1, options).FirstOrDefault()?.Card;
        if (card is null)
        {
            return Array.Empty<RewardDetail>();
        }

        return new[] { CreateCardDetail("随机稀有牌", card) };
    }

    private static IReadOnlyList<RewardDetail> PreviewGoldenPearl(NeowOptionResult option)
    {
        var gold = TryGetGoldValue(option.ModelId);
        return gold > 0 ? new[] { CreateGoldDetail("金币", gold) } : Array.Empty<RewardDetail>();
    }

    private static IReadOnlyList<RewardDetail> PreviewCursedPearl(NeowOptionResult option)
    {
        var details = new List<RewardDetail>();
        var gold = TryGetGoldValue(option.ModelId);
        if (gold > 0)
        {
            details.Add(CreateGoldDetail("金币", gold));
        }

        var greed = ModelDb.Card<Greed>();
        details.Add(CreateCardDetail("新增诅咒", greed));
        return details;
    }

    private static IReadOnlyList<RewardDetail> PreviewLostCoffer(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var options = new CardCreationOptions(
            new[] { player.Character.CardPool },
            CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter);
        var cards = CardFactory.CreateForReward(player, 3, options).Select(r => r.Card).ToList();
        var details = CreateCardDetails("卡牌奖励", cards).ToList();

        var potion = PotionFactory.CreateRandomPotionOutOfCombat(player, player.PlayerRng.Rewards);
        var potionTitle = LocalizationProvider.GetPotionTitle(potion.Id.Entry, potion.Title.GetFormattedText());
        details.Add(new RewardDetail(RewardDetailType.Potion, "随机药水", potionTitle, potion.Id.Entry));
        return details;
    }

    private static IReadOnlyList<RewardDetail> PreviewLeadPaperweight(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var options = new CardCreationOptions(
            new[] { ModelDb.CardPool<ColorlessCardPool>() },
            CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter);
        var cards = CardFactory.CreateForReward(player, 2, options).Select(r => r.Card);
        return CreateCardDetails("无色卡牌", cards).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewMassiveScroll(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var customPool = ModelDb.CardPool<ColorlessCardPool>()
            .GetUnlockedCards(player.RunState.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Concat(player.Character.CardPool.GetUnlockedCards(player.RunState.UnlockState, player.RunState.CardMultiplayerConstraint))
            .Where(c => c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly);
        var options = new CardCreationOptions(customPool, CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        var cards = CardFactory.CreateForReward(player, 3, options).Select(r => r.Card);
        return CreateCardDetails("多人卡牌", cards).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewNeowsTorment(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var card = context.State.CreateCard<NeowsFury>(context.Player);
        return new[] { CreateCardDetail("新增卡牌", card) };
    }

    private static IReadOnlyList<RewardDetail> PreviewLargeCapsule(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var cards = new List<CardModel?>
        {
            GetBasicCardByTag(player.Character, CardTag.Strike),
            GetBasicCardByTag(player.Character, CardTag.Defend)
        };
        return CreateCardDetails("新增卡牌", cards.OfType<CardModel>()).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewLeafyPoultice(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var deck = PileType.Deck.GetPile(player).Cards.Where(c => c.Rarity == CardRarity.Basic).ToList();
        var strike = deck.FirstOrDefault(c => c.Tags.Contains(CardTag.Strike));
        var defend = deck.FirstOrDefault(c => c.Tags.Contains(CardTag.Defend));
        var rng = player.PlayerRng.Transformations;
        var details = new List<RewardDetail>();
        foreach (var card in new[] { strike, defend })
        {
            if (card == null)
            {
                continue;
            }

            var replacement = new CardTransformation(card).GetReplacement(rng);
            if (replacement == null)
            {
                continue;
            }

            var text = $"{GetCardName(card)} ({card.Id.Entry}) → {GetCardName(replacement)} ({replacement.Id.Entry})";
            details.Add(CreateCardDetail("被转化", card));
            details.Add(CreateCardDetail("获得", replacement));
            details.Add(new RewardDetail(RewardDetailType.Text, "转化", text));
        }

        return details;
    }

    private static IReadOnlyList<RewardDetail> PreviewScrollBoxes(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var bundles = ScrollBoxes.GenerateRandomBundles(player);
        var details = new List<RewardDetail>();
        int index = 1;
        foreach (var bundle in bundles)
        {
            details.AddRange(CreateCardDetails($"卡牌组合{index}", bundle));
            index++;
        }

        return details;
    }

    private static IEnumerable<RewardDetail> CreateCardDetails(string label, IEnumerable<CardModel> cards)
    {
        foreach (var card in cards)
        {
            if (card == null)
            {
                continue;
            }

            var name = GetCardName(card);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new RewardDetail(RewardDetailType.Card, label, name, card.Id.Entry);
        }
    }

    private static RewardDetail CreateCardDetail(string label, CardModel card)
    {
        return new RewardDetail(RewardDetailType.Card, label, GetCardName(card), card.Id.Entry);
    }

    private static RewardDetail CreateGoldDetail(string label, int amount)
    {
        return new RewardDetail(RewardDetailType.Gold, label, amount.ToString(CultureInfo.InvariantCulture), null, amount);
    }

    private static string GetCardName(CardModel card)
    {
        return LocalizationProvider.GetCardTitle(card.Id.Entry, card.Title);
    }

    private static int TryGetGoldValue(ModelId modelId)
    {
        var relic = ModelDb.GetById<RelicModel>(modelId);
        return relic.DynamicVars.TryGetValue("Gold", out var goldVar) ? (int)goldVar.BaseValue : 0;
    }

    private static CardModel? GetBasicCardByTag(CharacterModel character, CardTag tag)
    {
        return character.CardPool.AllCards.FirstOrDefault(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(tag));
    }
}

internal sealed class OptionFilter
{
    private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    private OptionFilter(
        NeowChoiceKind? kind,
        IReadOnlyList<string> relicTerms,
        IReadOnlyList<string> relicIds,
        IReadOnlyList<string> cardIds,
        IReadOnlyList<string> potionIds,
        bool hasCriteria)
    {
        Kind = kind;
        RelicTerms = relicTerms;
        RelicIds = relicIds;
        CardIds = cardIds;
        PotionIds = potionIds;
        HasCriteria = hasCriteria;
    }

    public NeowChoiceKind? Kind { get; }
    public bool HasCriteria { get; }
    private IReadOnlyList<string> RelicTerms { get; }
    private IReadOnlyList<string> RelicIds { get; }
    private IReadOnlyList<string> CardIds { get; }
    private IReadOnlyList<string> PotionIds { get; }

    public static OptionFilter Create(
        NeowChoiceKind? kind,
        IEnumerable<string> relicTerms,
        IEnumerable<string> relicIds,
        IEnumerable<string> cardIds,
        IEnumerable<string> potionIds)
    {
        var relicList = NormalizeTerms(relicTerms);
        var relicIdList = NormalizeTerms(relicIds);
        var cardIdList = NormalizeTerms(cardIds);
        var potionIdList = NormalizeTerms(potionIds);

        var hasCriteria =
            kind.HasValue ||
            relicList.Count > 0 ||
            relicIdList.Count > 0 ||
            cardIdList.Count > 0 ||
            potionIdList.Count > 0;

        return new OptionFilter(kind, relicList, relicIdList, cardIdList, potionIdList, hasCriteria);
    }

    public bool Matches(NeowOptionResult option, string title, string description)
    {
        if (Kind.HasValue && option.Kind != Kind.Value)
        {
            return false;
        }

        if (RelicIds.Count > 0 && !RelicIds.Contains(option.ModelId.Entry, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (RelicTerms.Count > 0 && !MatchesRelic(option, title, description))
        {
            return false;
        }

        if (CardIds.Count > 0 && !MatchesDetailIds(option.Details, RewardDetailType.Card, CardIds))
        {
            return false;
        }

        if (PotionIds.Count > 0 && !MatchesDetailIds(option.Details, RewardDetailType.Potion, PotionIds))
        {
            return false;
        }

        return true;
    }

    private bool MatchesRelic(NeowOptionResult option, string title, string description)
    {
        foreach (var term in RelicTerms)
        {
            if (Contains(option.ModelId.Entry, term) ||
                Contains(option.DisplayName, term) ||
                Contains(title, term) ||
                Contains(description, term) ||
                Contains(option.Note, term))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDetailIds(IReadOnlyList<RewardDetail> details, RewardDetailType type, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return true;
        }

        foreach (var target in ids)
        {
            var found = false;
            foreach (var detail in details)
            {
                if (detail.Type != type || string.IsNullOrWhiteSpace(detail.ModelId))
                {
                    continue;
                }

                if (detail.ModelId.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> NormalizeTerms(IEnumerable<string> terms)
    {
        return terms?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private static bool Contains(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.IndexOf(term, Comparison) >= 0;
    }
}

internal readonly record struct NeowOptionResult(NeowChoiceKind Kind, ModelId ModelId, string DisplayName, string? Note, Type RelicType, IReadOnlyList<RewardDetail> Details);

internal sealed record SeedRollResult(string Seed, IReadOnlyList<NeowOptionResult> Options);

public enum NeowChoiceKind
{
    Positive,
    Curse
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum RewardDetailType
{
    Text,
    Card,
    Potion,
    Gold
}


internal sealed record RewardDetail(RewardDetailType Type, string Label, string Value, string? ModelId = null, int? Amount = null)
{
    public string FormatForDisplay()
    {
        var renderedValue = Value;
        if (!string.IsNullOrWhiteSpace(ModelId))
        {
            renderedValue = string.IsNullOrWhiteSpace(Value) ? ModelId! : $"{Value} ({ModelId})";
        }
        else if (Amount.HasValue && string.IsNullOrWhiteSpace(Value))
        {
            renderedValue = Amount.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Label))
        {
            return $"{Label}：{renderedValue}";
        }

        return renderedValue;
    }

    [JsonIgnore]
    public string SearchText
    {
        get
        {
            var builder = string.IsNullOrWhiteSpace(Label) ? Value : $"{Label} {Value}";
            if (!string.IsNullOrWhiteSpace(ModelId))
            {
                builder = $"{builder} {ModelId}";
            }
            if (Amount.HasValue)
            {
                builder = $"{builder} {Amount.Value}";
            }

            return builder;
        }
    }
}



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

    public RollResultWriter(OptionFilter filter, string outputPath)
    {
        _filter = filter;
        _outputPath = string.IsNullOrWhiteSpace(outputPath) ? SeedRollerDefaults.DefaultResultJson : outputPath;
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

    public void Complete()
    {
        var absolute = OutputPath;
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new RollResults(DateTimeOffset.Now, _totalSeeds, _entries.Count, _matchedOptions, _entries);
        using (var stream = File.Create(absolute))
        {
            JsonSerializer.Serialize(stream, payload, JsonOptions);
        }

        if (_entries.Count == 0 && _filter.HasCriteria)
        {
            RollerLog.Warning($"共检查 {_totalSeeds} 个种子，但当前筛选条件没有命中。已生成空的结果文件：{absolute}");
            RollerLog.Info("提示：可调整 config.json 中的筛选条件后重试。");
        }
        else
        {
            RollerLog.Info($"共检查 {_totalSeeds} 个种子，其中 {_entries.Count} 个符合条件（共 {_matchedOptions} 个选项）。结果已保存到：{absolute}");
        }
    }

    private List<SerializableOption> BuildOptions(SeedRollResult result)
    {
        var list = new List<SerializableOption>();
        foreach (var option in result.Options)
        {
            var (title, description) = LocalizationProvider.GetRelicStrings(option.ModelId, option.DisplayName);
            if (_filter.HasCriteria && !_filter.Matches(option, title, description))
            {
                continue;
            }

            list.Add(new SerializableOption(
                option.Kind.ToString(),
                option.ModelId.Entry,
                title,
                description,
                option.Note,
                option.Details.ToList()));
        }

        return list;
    }
}

internal sealed record SerializableSeedResult(string Seed, IReadOnlyList<SerializableOption> Options);

internal sealed record SerializableOption(string Kind, string RelicId, string Title, string Description, string? Note, IReadOnlyList<RewardDetail> Details);

internal sealed record RollResults(DateTimeOffset GeneratedAt, int TotalSeeds, int MatchedSeeds, int MatchedOptions, IReadOnlyList<SerializableSeedResult> Seeds);

internal sealed record InfoCatalog(
    DateTimeOffset? GeneratedAt,
    string? Language,
    IReadOnlyList<OptionInfo>? Options,
    IReadOnlyList<NamedEntry>? Cards,
    IReadOnlyList<NamedEntry>? Potions);

internal sealed record OptionInfo(string RelicId, string? Kind, string? Title, string? Description, string? Note);

internal sealed record NamedEntry(string Id, string? Name);

internal static class LocalizationProvider
{
    private static readonly Dictionary<string, RelicEntry> RelicEntries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> CardTitles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PotionTitles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex RichTextRegex = new(@"\[(?:/?)[^\]]+\]", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions SeedInfoJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static bool _initialized;

    public static void Initialize(string? seedInfoPath)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var path = ResolveSeedInfoPath(seedInfoPath);
        if (path is null)
        {
            RollerLog.Warning("未找到 seed_info.json，输出名称将使用英文。");
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var catalog = JsonSerializer.Deserialize<InfoCatalog>(stream, SeedInfoJsonOptions);
            if (catalog == null)
            {
                RollerLog.Warning($"解析 seed_info.json 失败：{path}");
                return;
            }

            if (catalog.Options != null)
            {
                foreach (var option in catalog.Options)
                {
                    if (string.IsNullOrWhiteSpace(option.RelicId))
                    {
                        continue;
                    }

                    var entry = GetOrCreate(option.RelicId);
                    entry.Title = Sanitize(option.Title);
                    entry.Description = string.IsNullOrWhiteSpace(option.Description) ? string.Empty : option.Description!;
                }
            }

            if (catalog.Cards != null)
            {
                foreach (var card in catalog.Cards)
                {
                    if (!string.IsNullOrWhiteSpace(card.Id) && !string.IsNullOrWhiteSpace(card.Name))
                    {
                        CardTitles[card.Id] = card.Name!;
                    }
                }
            }

            if (catalog.Potions != null)
            {
                foreach (var potion in catalog.Potions)
                {
                    if (!string.IsNullOrWhiteSpace(potion.Id) && !string.IsNullOrWhiteSpace(potion.Name))
                    {
                        PotionTitles[potion.Id] = potion.Name!;
                    }
                }
            }

            RollerLog.Info($"已加载 seed_info：{path}");
        }
        catch (Exception exception)
        {
            RollerLog.Warning($"读取 seed_info.json 失败：{exception.Message}");
        }
    }

    public static (string Title, string Description) GetRelicStrings(ModelId modelId, string fallbackTitle)
    {
        if (RelicEntries.TryGetValue(modelId.Entry, out var entry))
        {
            var title = string.IsNullOrWhiteSpace(entry.Title) ? Sanitize(fallbackTitle) : entry.Title!;
            var description = entry.Description ?? string.Empty;
            return (string.IsNullOrWhiteSpace(title) ? fallbackTitle : title, description);
        }

        var sanitized = Sanitize(fallbackTitle);
        return (string.IsNullOrWhiteSpace(sanitized) ? fallbackTitle : sanitized, string.Empty);
    }

    public static string GetCardTitle(string modelId, string fallback)
    {
        if (CardTitles.TryGetValue(modelId, out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var sanitized = Sanitize(fallback);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public static string GetPotionTitle(string modelId, string fallback)
    {
        if (PotionTitles.TryGetValue(modelId, out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var sanitized = Sanitize(fallback);
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static RelicEntry GetOrCreate(string key)
    {
        if (!RelicEntries.TryGetValue(key, out var entry))
        {
            entry = new RelicEntry();
            RelicEntries[key] = entry;
        }

        return entry;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutTags = RichTextRegex.Replace(value, string.Empty);
        withoutTags = withoutTags.Replace("\r\n", "\n");
        withoutTags = withoutTags.Replace("\n", Environment.NewLine);
        withoutTags = withoutTags.Replace("\\n", Environment.NewLine);
        return withoutTags.Trim();
    }

    private static string? ResolveSeedInfoPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var full = Path.GetFullPath(overridePath);
            if (File.Exists(full))
            {
                return full;
            }
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultSeedInfo);
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        return null;
    }

    private sealed class RelicEntry
    {
        public string? Title;
        public string? Description;
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
        SeedInfoPath = string.IsNullOrWhiteSpace(seedInfoPath)
            ? Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultSeedInfo)
            : seedInfoPath!;
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

    public string SeedInfoPath { get; }

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
        var seedInfoPath = FirstNonEmpty(Arg("seed-info"), config?.SeedInfoPath, SeedRollerDefaults.DefaultSeedInfo);
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
    Random
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
            for (int i = 0; i < options.Count; i++)
            {
                yield return SeedHelper.GetRandomSeed();
            }

            yield break;
        }

        string current = options.StartSeed;
        for (int i = 0; i < options.Count; i++)
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


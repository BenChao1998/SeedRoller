using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace SeedRollerCli;

internal static class RewardPreviewProvider
{
    private static readonly RewardDetail[] Empty = [];

    public static IReadOnlyList<RewardDetail> GetDetails(NeowOptionResult option, Func<RunContext> contextFactory)
    {
        try
        {
            var type = option.RelicType;
            if (type == typeof(ArcaneScroll))
                return PreviewArcaneScroll(contextFactory);
            if (type == typeof(GoldenPearl))
                return PreviewGoldenPearl(option);
            if (type == typeof(CursedPearl))
                return PreviewCursedPearl(option);
            if (type == typeof(LostCoffer))
                return PreviewLostCoffer(contextFactory);
            if (type == typeof(LeadPaperweight))
                return PreviewLeadPaperweight(contextFactory);
            if (type == typeof(MassiveScroll))
                return PreviewMassiveScroll(contextFactory);
            if (type == typeof(NeowsTorment))
                return PreviewNeowsTorment(contextFactory);
            if (type == typeof(LargeCapsule))
                return PreviewLargeCapsule(contextFactory);
            if (type == typeof(LeafyPoultice))
                return PreviewLeafyPoultice(contextFactory);
            if (type == typeof(ScrollBoxes))
                return PreviewScrollBoxes(contextFactory);
        }
        catch (Exception ex)
        {
            RollerLog.Warning($"奖励预览失败（{option.ModelId.Entry}）：{ex.Message}");
        }

        return Empty;
    }

    private static IReadOnlyList<RewardDetail> PreviewArcaneScroll(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var options = new CardCreationOptions(
            new[] { player.Character.CardPool },
            CardCreationSource.Other,
            CardRarityOddsType.Uniform,
            static card => card.Rarity == CardRarity.Rare).WithFlags(CardCreationFlags.NoUpgradeRoll);

        var card = CardFactory.CreateForReward(player, 1, options)
            .FirstOrDefault()?.Card;

        return card is null
            ? Empty
            : new[] { CreateCardDetail("随机稀有牌", card) };
    }

    private static IReadOnlyList<RewardDetail> PreviewGoldenPearl(NeowOptionResult option)
    {
        var value = TryGetGoldValue(option.ModelId);
        if (value <= 0)
        {
            return Empty;
        }

        return new[] { CreateGoldDetail("金币", value) };
    }

    private static IReadOnlyList<RewardDetail> PreviewCursedPearl(NeowOptionResult option)
    {
        var details = new List<RewardDetail>();
        var value = TryGetGoldValue(option.ModelId);
        if (value > 0)
        {
            details.Add(CreateGoldDetail("金币", value));
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
        var cards = CardFactory.CreateForReward(player, 3, options)
            .Select(result => result.Card)
            .ToList();

        var details = CreateCardDetails("卡牌奖励", cards).ToList();
        var potion = PotionFactory.CreateRandomPotionOutOfCombat(player, player.PlayerRng.Rewards, null);
        if (potion is not null)
        {
            var modelId = potion.Id.Entry;
            var title = LocalizationProvider.GetPotionTitle(modelId, potion.Title.GetFormattedText());
            details.Add(new RewardDetail(RewardDetailType.Potion, "随机药水", title, modelId));
        }

        return details;
    }

    private static IReadOnlyList<RewardDetail> PreviewLeadPaperweight(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var colorlessPool = ModelDb.CardPool<ColorlessCardPool>();
        var unlockState = player.RunState.UnlockState;
        var constraint = player.RunState.CardMultiplayerConstraint;
        var unlocked = colorlessPool.GetUnlockedCards(unlockState, constraint);

        var options = new CardCreationOptions(
            unlocked,
            CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter);

        var cards = CardFactory.CreateForReward(player, 2, options)
            .Select(result => result.Card);

        return CreateCardDetails("无色卡牌", cards).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewMassiveScroll(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var colorlessPool = ModelDb.CardPool<ColorlessCardPool>();
        var unlock = player.RunState.UnlockState;
        var constraint = player.RunState.CardMultiplayerConstraint;

        var unlockedColorless = colorlessPool.GetUnlockedCards(unlock, constraint);
        var unlockedCharacter = player.Character.CardPool.GetUnlockedCards(unlock, constraint);
        var cardsPool = unlockedColorless
            .Concat(unlockedCharacter)
            .Where(static card => card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly);

        var options = new CardCreationOptions(cardsPool, CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        var cards = CardFactory.CreateForReward(player, 3, options)
            .Select(result => result.Card);

        return CreateCardDetails("多人卡牌", cards).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewNeowsTorment(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var fury = context.State.CreateCard<NeowsFury>(context.Player);
        return new[] { CreateCardDetail("新增卡牌", fury) };
    }

    private static IReadOnlyList<RewardDetail> PreviewLargeCapsule(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var cards = new List<CardModel>();

        var strike = GetBasicCardByTag(player.Character, CardTag.Strike);
        if (strike is not null)
        {
            cards.Add(strike);
        }

        var defend = GetBasicCardByTag(player.Character, CardTag.Defend);
        if (defend is not null)
        {
            cards.Add(defend);
        }

        return CreateCardDetails("新增卡牌", cards).ToList();
    }

    private static IReadOnlyList<RewardDetail> PreviewLeafyPoultice(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var player = context.Player;
        var deck = PileType.Deck.GetPile(player).Cards;
        var basicCards = deck.Where(static card => card.Rarity == CardRarity.Basic).ToList();

        var strike = basicCards.FirstOrDefault(card => card.Tags.Contains(CardTag.Strike));
        var defend = basicCards.FirstOrDefault(card => card.Tags.Contains(CardTag.Defend));

        var rng = player.PlayerRng.Transformations;
        var results = new List<RewardDetail>();

        foreach (var original in new[] { strike, defend })
        {
            if (original is null)
            {
                continue;
            }

            var transformation = new CardTransformation(original);
            var replacement = transformation.GetReplacement(rng);
            if (replacement is null)
            {
                continue;
            }

            var summary = $"{GetCardName(original)} ({original.Id.Entry}) → {GetCardName(replacement)} ({replacement.Id.Entry})";

            results.Add(CreateCardDetail("被转化", original));
            results.Add(CreateCardDetail("获得", replacement));
            results.Add(new RewardDetail(RewardDetailType.Text, "转化", summary));
        }

        return results;
    }

    private static IReadOnlyList<RewardDetail> PreviewScrollBoxes(Func<RunContext> contextFactory)
    {
        var context = contextFactory();
        var bundles = ScrollBoxes.GenerateRandomBundles(context.Player);
        var details = new List<RewardDetail>();
        var index = 1;

        foreach (var bundle in bundles)
        {
            var label = $"卡牌组合{index}";
            details.AddRange(CreateCardDetails(label, bundle));
            index++;
        }

        return details;
    }

    private static IEnumerable<RewardDetail> CreateCardDetails(string label, IEnumerable<CardModel> cards)
    {
        foreach (var card in cards)
        {
            if (card is not null)
            {
                yield return CreateCardDetail(label, card);
            }
        }
    }

    private static RewardDetail CreateCardDetail(string label, CardModel card)
    {
        return new RewardDetail(
            RewardDetailType.Card,
            label,
            GetCardName(card),
            card.Id.Entry);
    }

    private static RewardDetail CreateGoldDetail(string label, int amount)
    {
        return new RewardDetail(
            RewardDetailType.Gold,
            label,
            amount.ToString(CultureInfo.InvariantCulture),
            null,
            amount);
    }

    private static string GetCardName(CardModel card)
    {
        var modelId = card.Id.Entry;
        var fallback = card.Title ?? string.Empty;
        return LocalizationProvider.GetCardTitle(modelId, fallback);
    }

    private static int TryGetGoldValue(ModelId modelId)
    {
        var relic = ModelDb.GetById<RelicModel>(modelId);
        if (relic.DynamicVars.TryGetValue("Gold", out DynamicVar? value))
        {
            return (int)value.BaseValue;
        }

        return 0;
    }

    private static CardModel? GetBasicCardByTag(CharacterModel character, CardTag tag)
    {
        return character.CardPool.AllCards
            .FirstOrDefault(card => card.Rarity == CardRarity.Basic && card.Tags.Contains(tag));
    }
}

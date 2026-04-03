using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedRollerCli;

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
        IEnumerable<string>? relicTerms,
        IEnumerable<string>? relicIds,
        IEnumerable<string>? cardIds,
        IEnumerable<string>? potionIds)
    {
        var normalizedRelicTerms = NormalizeTerms(relicTerms);
        var normalizedRelicIds = NormalizeTerms(relicIds);
        var normalizedCardIds = NormalizeTerms(cardIds);
        var normalizedPotionIds = NormalizeTerms(potionIds);

        var hasCriteria =
            kind.HasValue ||
            normalizedRelicTerms.Count > 0 ||
            normalizedRelicIds.Count > 0 ||
            normalizedCardIds.Count > 0 ||
            normalizedPotionIds.Count > 0;

        return new OptionFilter(
            kind,
            normalizedRelicTerms,
            normalizedRelicIds,
            normalizedCardIds,
            normalizedPotionIds,
            hasCriteria);
    }

    public bool Matches(NeowOptionResult option, string title, string description)
    {
        if (Kind.HasValue && option.Kind != Kind.Value)
        {
            return false;
        }

        if (RelicIds.Count > 0)
        {
            var modelId = option.ModelId.Entry;
            if (!RelicIds.Contains(modelId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (RelicTerms.Count > 0 && !MatchesRelic(option, title, description))
        {
            return false;
        }

        if (CardIds.Count > 0 &&
            !MatchesDetailIds(option.Details, RewardDetailType.Card, CardIds))
        {
            return false;
        }

        if (PotionIds.Count > 0 &&
            !MatchesDetailIds(option.Details, RewardDetailType.Potion, PotionIds))
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

    private static bool MatchesDetailIds(
        IReadOnlyList<RewardDetail> details,
        RewardDetailType type,
        IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            return true;
        }

        var requiredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            requiredCounts[id] = requiredCounts.TryGetValue(id, out var count) ? count + 1 : 1;
        }

        if (requiredCounts.Count == 0)
        {
            return true;
        }

        var availableCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var detail in details)
        {
            if (detail.Type != type || string.IsNullOrWhiteSpace(detail.ModelId))
            {
                continue;
            }

            var key = detail.ModelId;
            availableCounts[key] = availableCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        foreach (var requirement in requiredCounts)
        {
            if (!availableCounts.TryGetValue(requirement.Key, out var available) || available < requirement.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> NormalizeTerms(IEnumerable<string>? terms)
    {
        if (terms == null)
        {
            return new List<string>();
        }

        var filtered = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return filtered.Count > 0 ? filtered : new List<string>();
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

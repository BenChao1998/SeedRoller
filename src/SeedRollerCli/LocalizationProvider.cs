using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;

namespace SeedRollerCli;

internal static class LocalizationProvider
{
    private static readonly Regex RichTextRegex = new(@"\[/?color[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Dictionary<string, RelicEntry> RelicEntries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> CardTitles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PotionTitles = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static void Initialize(string? seedInfoPath)
    {
        if (_initialized)
        {
            return;
        }

        var catalog = SeedInfoCatalogProvider.EnsureLoaded(seedInfoPath, out var sourcePath);
        if (catalog == null)
        {
            if (!string.IsNullOrWhiteSpace(seedInfoPath))
            {
                RollerLog.Warning($"未找到 seed_info.json：{seedInfoPath}");
            }
            else
            {
                RollerLog.Warning("未找到 seed_info.json，奖项名称将回退到英文。");
            }

            _initialized = true;
            return;
        }

        RelicEntries.Clear();
        CardTitles.Clear();
        PotionTitles.Clear();

        foreach (var option in catalog.Options ?? Array.Empty<SeedInfoOption>())
        {
            if (string.IsNullOrWhiteSpace(option.RelicId))
            {
                continue;
            }

            var entry = GetOrCreate(option.RelicId);
            entry.Title = Sanitize(option.Title);
            entry.Description = string.IsNullOrWhiteSpace(option.Description) ? string.Empty : option.Description!;
        }

        foreach (var card in catalog.Cards ?? Array.Empty<SeedInfoEntry>())
        {
            if (!string.IsNullOrWhiteSpace(card.Id) && !string.IsNullOrWhiteSpace(card.Name))
            {
                CardTitles[card.Id] = card.Name!;
            }
        }

        foreach (var potion in catalog.Potions ?? Array.Empty<SeedInfoEntry>())
        {
            if (!string.IsNullOrWhiteSpace(potion.Id) && !string.IsNullOrWhiteSpace(potion.Name))
            {
                PotionTitles[potion.Id] = potion.Name!;
            }
        }

        RollerLog.Info($"已加载 seed_info：{sourcePath ?? "embedded seed_info.json"}");
        _initialized = true;
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
        withoutTags = withoutTags.Replace("\r\n", "\n", StringComparison.Ordinal);
        withoutTags = withoutTags.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        withoutTags = withoutTags.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
        return withoutTags.Trim();
    }

    private sealed class RelicEntry
    {
        public string? Title;
        public string? Description;
    }
}

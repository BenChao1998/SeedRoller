using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SeedRollerCli;

public sealed record SeedInfoCatalog(
    DateTimeOffset? GeneratedAt,
    string? Language,
    IReadOnlyList<SeedInfoOption> Options,
    IReadOnlyList<SeedInfoEntry> Cards,
    IReadOnlyList<SeedInfoEntry> Potions);

public sealed record SeedInfoOption(string RelicId, string? Kind, string? Title, string? Description, string? Note);

public sealed record SeedInfoEntry(string Id, string? Name);

public static class SeedInfoCatalogProvider
{
    private const string EmbeddedResourceName = "SeedRollerCli.seed_info.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly object SyncRoot = new();
    private static SeedInfoCatalog? _cachedCatalog;
    private static string? _cachedSource;

    public static SeedInfoCatalog? EnsureLoaded() => EnsureLoaded(null, out _);

    public static SeedInfoCatalog? EnsureLoaded(string? seedInfoPath) => EnsureLoaded(seedInfoPath, out _);

    public static SeedInfoCatalog? EnsureLoaded(string? seedInfoPath, out string? sourceDescription)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(seedInfoPath) && _cachedCatalog != null)
            {
                sourceDescription = _cachedSource;
                return _cachedCatalog;
            }

            var catalog = LoadCatalog(seedInfoPath, out sourceDescription);
            if (catalog != null && string.IsNullOrWhiteSpace(seedInfoPath))
            {
                _cachedCatalog = catalog;
                _cachedSource = sourceDescription;
            }

            return catalog;
        }
    }

    private static SeedInfoCatalog? LoadCatalog(string? overridePath, out string? sourceDescription)
    {
        using var stream = OpenStream(overridePath, out sourceDescription);
        if (stream == null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<SeedInfoCatalog>(stream, JsonOptions);
    }

    private static Stream? OpenStream(string? overridePath, out string? sourceDescription)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var full = Path.GetFullPath(overridePath);
            sourceDescription = full;
            return File.Exists(full) ? File.OpenRead(full) : null;
        }

        var fallbackPath = Path.Combine(AppContext.BaseDirectory, SeedRollerDefaults.DefaultSeedInfo);
        if (File.Exists(fallbackPath))
        {
            sourceDescription = fallbackPath;
            return File.OpenRead(fallbackPath);
        }

        var assembly = typeof(SeedInfoCatalogProvider).Assembly;
        var resourceStream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (resourceStream != null)
        {
            sourceDescription = "embedded seed_info.json";
            return resourceStream;
        }

        sourceDescription = null;
        return null;
    }
}

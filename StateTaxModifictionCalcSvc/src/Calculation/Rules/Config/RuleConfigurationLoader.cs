using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Calculation.Rules.Config;

/// <summary>
/// Loads rule definitions from JSON files at startup.
/// Rules can be updated by replacing JSON files and restarting the service
/// (or by implementing hot-reload via IOptionsMonitor).
///
/// File layout convention:
///   rules/
///     default/         — rules for all jurisdictions
///       gilti.json
///       subpart_f.json
///       163j.json
///     states/
///       CA.json        — California-specific rule overrides
///       NY.json
///       IL.json
///     local/
///       NYC.json
/// </summary>
public sealed class RuleConfigurationLoader
{
    private readonly ILogger<RuleConfigurationLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public RuleConfigurationLoader(ILogger<RuleConfigurationLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads all rule definitions from the specified root directory.
    /// Returns all definitions across all files, deduplicated by RuleId.
    /// Later files override earlier files with the same RuleId.
    /// </summary>
    public IReadOnlyList<RuleDefinition> LoadAll(string rulesRootDirectory)
    {
        var definitions = new Dictionary<string, RuleDefinition>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(rulesRootDirectory))
        {
            _logger.LogWarning("Rules directory not found: {Dir}", rulesRootDirectory);
            return [];
        }

        foreach (var file in Directory.EnumerateFiles(rulesRootDirectory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(f => f)) // deterministic load order
        {
            try
            {
                var json = File.ReadAllText(file);
                var fileDefs = JsonSerializer.Deserialize<List<RuleDefinition>>(json, JsonOptions)
                               ?? [];

                foreach (var def in fileDefs)
                {
                    if (string.IsNullOrWhiteSpace(def.RuleId))
                    {
                        _logger.LogWarning("Rule definition in {File} has empty RuleId — skipped.", file);
                        continue;
                    }
                    definitions[def.RuleId] = def;
                    _logger.LogDebug("Loaded rule {RuleId} from {File}", def.RuleId, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load rule definitions from {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} rule definitions from {Dir}",
            definitions.Count, rulesRootDirectory);

        var result = definitions.Values.ToList().AsReadOnly();
        WarnOnOverlaps(result);
        return result;
    }

    /// <summary>
    /// Detects rules that share a CategoryCode and whose jurisdiction+year applicability
    /// overlaps. An overlap means BaseJurisdictionStrategy would pick one arbitrarily
    /// (FirstOrDefault on Priority=100 ties), silently producing wrong results.
    ///
    /// This is a startup-time configuration guard — not a runtime check.
    /// Fix: use JurisdictionRateOverrides on a single rule instead of separate rules per year.
    /// </summary>
    private void WarnOnOverlaps(IReadOnlyList<RuleDefinition> rules)
    {
        // Group by category; within each group check every pair for jurisdiction+year overlap.
        var byCategory = rules.GroupBy(r => r.CategoryCode, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byCategory)
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];

                if (YearRangesOverlap(a, b) && JurisdictionsOverlap(a, b))
                {
                    _logger.LogWarning(
                        "Rule overlap detected for category '{Category}': " +
                        "'{RuleA}' (years {A1}-{A2}, jurisdictions {AJ}) and " +
                        "'{RuleB}' (years {B1}-{B2}, jurisdictions {BJ}) can both match " +
                        "the same calculation. Only one will fire (Priority order). " +
                        "Consolidate into JurisdictionRateOverrides on a single rule.",
                        group.Key,
                        a.RuleId, a.EffectiveFrom, a.EffectiveTo?.ToString() ?? "∞", string.Join(",", a.AppliesToJurisdictions),
                        b.RuleId, b.EffectiveFrom, b.EffectiveTo?.ToString() ?? "∞", string.Join(",", b.AppliesToJurisdictions));
                }
            }
        }
    }

    private static bool YearRangesOverlap(RuleDefinition a, RuleDefinition b)
    {
        var aTo = a.EffectiveTo ?? int.MaxValue;
        var bTo = b.EffectiveTo ?? int.MaxValue;
        return a.EffectiveFrom <= bTo && b.EffectiveFrom <= aTo;
    }

    private static bool JurisdictionsOverlap(RuleDefinition a, RuleDefinition b)
    {
        var aAll = a.AppliesToJurisdictions.Contains("ALL", StringComparer.OrdinalIgnoreCase);
        var bAll = b.AppliesToJurisdictions.Contains("ALL", StringComparer.OrdinalIgnoreCase);

        if (aAll && bAll) return true;
        if (aAll) return !b.AppliesToJurisdictions.All(j => a.ExcludedJurisdictions.Contains(j, StringComparer.OrdinalIgnoreCase));
        if (bAll) return !a.AppliesToJurisdictions.All(j => b.ExcludedJurisdictions.Contains(j, StringComparer.OrdinalIgnoreCase));

        return a.AppliesToJurisdictions.Intersect(b.AppliesToJurisdictions, StringComparer.OrdinalIgnoreCase).Any();
    }
}

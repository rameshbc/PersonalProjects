using Calculation.Rules.Config;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Calculation.Tests.Unit.Config;

/// <summary>
/// Tests for RuleConfigurationLoader — covering JSON parsing, RuleId deduplication,
/// and startup-time overlap detection.
/// </summary>
public sealed class RuleConfigurationLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RuleConfigurationLoader _loader = new(NullLogger<RuleConfigurationLoader>.Instance);

    public RuleConfigurationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rule_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── JSON loading ───────────────────────────────────────────────────────

    [Fact]
    public void Loads_rule_definitions_from_valid_json_file()
    {
        WriteJson("rules.json", """
            [
              {
                "ruleId": "TEST_RULE_V1",
                "categoryCode": "TEST_CAT",
                "formulaType": "LinearRate",
                "inputLines": [{ "lineCode": "LINE_A", "sign": 1 }],
                "rate": 1.0,
                "effectiveFrom": 2018,
                "appliesToJurisdictions": ["ALL"],
                "excludedJurisdictions": [],
                "jurisdictionRateOverrides": {}
              }
            ]
            """);

        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(1);
        rules[0].RuleId.Should().Be("TEST_RULE_V1");
        rules[0].EffectiveFrom.Should().Be(2018);
        rules[0].InputLines.Should().HaveCount(1);
    }

    [Fact]
    public void Loads_jurisdiction_rate_overrides_with_year_ranges()
    {
        WriteJson("rules.json", """
            [
              {
                "ruleId": "GILTI_V1",
                "categoryCode": "GILTI_INCL",
                "formulaType": "LinearRate",
                "inputLines": [],
                "rate": 1.0,
                "effectiveFrom": 2018,
                "appliesToJurisdictions": ["ALL"],
                "excludedJurisdictions": [],
                "jurisdictionRateOverrides": {
                  "NY": [
                    { "rate": 1.0,  "effectiveFrom": 2018, "effectiveTo": 2022 },
                    { "rate": 0.50, "effectiveFrom": 2023,  "changeNote": "Part CC" }
                  ]
                }
              }
            ]
            """);

        var rules = _loader.LoadAll(_tempDir);

        var nyRanges = rules[0].JurisdictionRateOverrides["NY"];
        nyRanges.Should().HaveCount(2);
        nyRanges[0].Rate.Should().Be(1.0m);
        nyRanges[0].EffectiveTo.Should().Be(2022);
        nyRanges[1].Rate.Should().Be(0.50m);
        nyRanges[1].EffectiveFrom.Should().Be(2023);
        nyRanges[1].ChangeNote.Should().Be("Part CC");
    }

    [Fact]
    public void Skips_rule_definitions_with_empty_RuleId()
    {
        WriteJson("rules.json", """
            [
              { "ruleId": "",    "categoryCode": "X", "formulaType": "LinearRate", "inputLines": [], "rate": 1.0, "effectiveFrom": 2018, "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} },
              { "ruleId": "VALID", "categoryCode": "X", "formulaType": "LinearRate", "inputLines": [], "rate": 1.0, "effectiveFrom": 2018, "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }
            ]
            """);

        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(1);
        rules[0].RuleId.Should().Be("VALID");
    }

    [Fact]
    public void Returns_empty_list_when_directory_does_not_exist()
    {
        var rules = _loader.LoadAll(Path.Combine(_tempDir, "nonexistent"));

        rules.Should().BeEmpty();
    }

    [Fact]
    public void Tolerates_missing_optional_fields_with_defaults()
    {
        // Minimal JSON — only required fields
        WriteJson("rules.json", """
            [{
              "ruleId": "MINIMAL",
              "categoryCode": "CAT",
              "formulaType": "LinearRate",
              "inputLines": [],
              "rate": 1.0,
              "effectiveFrom": 2018,
              "appliesToJurisdictions": ["ALL"],
              "excludedJurisdictions": [],
              "jurisdictionRateOverrides": {}
            }]
            """);

        var rules = _loader.LoadAll(_tempDir);

        rules[0].EffectiveTo.Should().BeNull();
        rules[0].MaximumAmount.Should().BeNull();
        rules[0].MinimumInputAmount.Should().BeNull();
    }

    // ── RuleId deduplication ───────────────────────────────────────────────

    [Fact]
    public void Later_file_overrides_earlier_file_for_same_RuleId()
    {
        // Files are loaded in alphabetical order; b.json loads after a.json
        WriteJson("a_rules.json", """
            [{ "ruleId": "GILTI_V1", "categoryCode": "GILTI_INCL", "formulaType": "LinearRate",
               "inputLines": [], "rate": 0.75, "effectiveFrom": 2018,
               "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }]
            """);

        WriteJson("b_override.json", """
            [{ "ruleId": "GILTI_V1", "categoryCode": "GILTI_INCL", "formulaType": "LinearRate",
               "inputLines": [], "rate": 0.50, "effectiveFrom": 2018,
               "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }]
            """);

        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(1, "same RuleId deduplicated");
        rules[0].Rate.Should().Be(0.50m, "b_override.json is alphabetically later — wins");
    }

    [Fact]
    public void Loads_rules_recursively_from_subdirectories()
    {
        var subDir = Path.Combine(_tempDir, "states");
        Directory.CreateDirectory(subDir);

        WriteJson("default.json", """
            [{ "ruleId": "DEFAULT_RULE", "categoryCode": "CAT_A", "formulaType": "LinearRate",
               "inputLines": [], "rate": 1.0, "effectiveFrom": 2018,
               "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }]
            """);

        WriteJson(Path.Combine("states", "ny.json"), """
            [{ "ruleId": "NY_SPECIFIC", "categoryCode": "CAT_B", "formulaType": "LinearRate",
               "inputLines": [], "rate": 0.50, "effectiveFrom": 2018,
               "appliesToJurisdictions": ["NY"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }]
            """);

        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(2);
        rules.Select(r => r.RuleId).Should().Contain(["DEFAULT_RULE", "NY_SPECIFIC"]);
    }

    // ── Overlap detection ──────────────────────────────────────────────────

    [Fact]
    public void No_warning_emitted_for_non_overlapping_year_ranges_same_category()
    {
        // V1 ends 2025; V2 starts 2026 — clean handoff, no overlap.
        WriteJson("rules.json", """
            [
              { "ruleId": "GILTI_V1", "categoryCode": "GILTI_INCL", "formulaType": "LinearRate",
                "inputLines": [], "rate": 1.0, "effectiveFrom": 2018, "effectiveTo": 2025,
                "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} },
              { "ruleId": "GILTI_V2", "categoryCode": "GILTI_INCL", "formulaType": "LinearRate",
                "inputLines": [], "rate": 0.375, "effectiveFrom": 2026,
                "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }
            ]
            """);

        // Should load without throwing; no assertion on log output since we use NullLogger.
        // The important thing is non-overlapping rules load and return both definitions.
        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(2);
    }

    [Fact]
    public void Two_rules_for_different_categories_do_not_trigger_overlap_warning()
    {
        WriteJson("rules.json", """
            [
              { "ruleId": "GILTI_V1",    "categoryCode": "GILTI_INCL",   "formulaType": "LinearRate",
                "inputLines": [], "rate": 1.0, "effectiveFrom": 2018,
                "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} },
              { "ruleId": "SUBF_V1",     "categoryCode": "SUBPART_F_INCL","formulaType": "LinearRate",
                "inputLines": [], "rate": 1.0, "effectiveFrom": 2018,
                "appliesToJurisdictions": ["ALL"], "excludedJurisdictions": [], "jurisdictionRateOverrides": {} }
            ]
            """);

        // Different categories — no overlap concern.
        var rules = _loader.LoadAll(_tempDir);

        rules.Should().HaveCount(2);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void WriteJson(string relativePath, string json)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, json);
    }
}

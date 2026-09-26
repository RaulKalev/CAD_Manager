using CAD_Manager.Core;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CAD_Manager.Core.Tests
{
    public sealed class LayerFilterRuleSuggesterTests
    {
        [Fact]
        public void Suggest_ProposesEnabledContainsRulesForHiddenLayersOnly()
        {
            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[]
                {
                    Dwg(new[] { "A-FURN" }, new[] { "A-WALL", "A-DOOR" })
                },
                null);

            LayerSelectionFilterRule rule = Assert.Single(rules);
            Assert.True(rule.IsEnabled);
            Assert.Equal(LayerNameMatchType.Contains, rule.MatchType);
            Assert.Equal("A-FURN", rule.Pattern);
            AssertSelectsOnlyHidden(rules, new[] { "A-FURN" }, new[] { "A-WALL", "A-DOOR" });
        }

        [Fact]
        public void Suggest_DropsSharedCompanyAndProjectPrefixes()
        {
            string[] hidden = { "ACME_P1234_E-LIGHT", "ACME_P1234_E-POWER" };
            string[] visible = { "ACME_P1234_A-WALL", "ACME_P1234_A-DOOR", "ACME_P1234_A-GRID" };

            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[] { Dwg(hidden, visible) },
                null);

            Assert.Equal(new[] { "E-LIGHT", "E-POWER" }, rules.Select(rule => rule.Pattern));
            AssertSelectsOnlyHidden(rules, hidden, visible);
        }

        [Fact]
        public void Suggest_MergesSameLayerFromDifferentCompanies()
        {
            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[]
                {
                    Dwg(new[] { "ACME_A-FURN" }, new[] { "ACME_A-WALL", "ACME_A-DOOR", "ACME_A-GRID" }),
                    Dwg(new[] { "BETA-A-FURN" }, new[] { "BETA-A-WALL", "BETA-A-DOOR", "BETA-A-GRID" })
                },
                null);

            Assert.Equal("A-FURN", Assert.Single(rules).Pattern);
        }

        [Fact]
        public void Suggest_GroupsNumberedVariantsUnderSharedPrefix()
        {
            string[] hidden = { "E-LIGHT-01", "E-LIGHT-02", "E-LIGHT-03" };
            string[] visible = { "E-POWER-01", "E-DATA" };

            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[] { Dwg(hidden, visible) },
                null);

            Assert.Equal("E-LIGHT", Assert.Single(rules).Pattern);
            AssertSelectsOnlyHidden(rules, hidden, visible);
        }

        [Fact]
        public void Suggest_KeepsSpecificNameWhenShortPatternWouldSelectVisibleLayer()
        {
            string[] hidden = { "ACME_A-WALL", "ACME_A-DOOR", "ACME_A-GRID" };
            string[] visible = { "ACME_A-WALL-EXT", "ACME_A-DOOR-TAG", "ACME_A-GRID-TEXT" };

            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[] { Dwg(hidden, visible) },
                null);

            Assert.Equal(3, rules.Count);
            Assert.All(rules, rule => Assert.Equal(LayerNameMatchType.Equals, rule.MatchType));
            AssertSelectsOnlyHidden(rules, hidden, visible);
        }

        [Fact]
        public void Suggest_StripsExternalReferencePrefixes()
        {
            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[]
                {
                    Dwg(new[] { "Site|C-TOPO", "Plan$0$C-TOPO" }, new[] { "C-ROAD" })
                },
                null);

            Assert.Equal("C-TOPO", Assert.Single(rules).Pattern);
        }

        [Fact]
        public void Suggest_DoesNotRepeatProposalsOnSecondImport()
        {
            DwgLayerVisibility[] dwgs =
            {
                Dwg(new[] { "ACME_E-LIGHT", "ACME_E-POWER" }, new[] { "ACME_A-WALL", "ACME_A-DOOR" })
            };

            IReadOnlyList<LayerSelectionFilterRule> first = LayerFilterRuleSuggester.Suggest(dwgs, null);
            IReadOnlyList<LayerSelectionFilterRule> second = LayerFilterRuleSuggester.Suggest(dwgs, first);

            Assert.NotEmpty(first);
            Assert.Empty(second);
        }

        [Fact]
        public void Suggest_SkipsLayersCoveredByUserRulesEvenWhenDisabled()
        {
            List<LayerSelectionFilterRule> existing = new List<LayerSelectionFilterRule>
            {
                new LayerSelectionFilterRule { IsEnabled = false, Pattern = "light" }
            };

            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[] { Dwg(new[] { "E-LIGHT", "E-POWER" }, new[] { "A-WALL" }) },
                existing);

            Assert.Equal("E-POWER", Assert.Single(rules).Pattern);
        }

        [Fact]
        public void Suggest_ReturnsNothingWhenNoLayersAreHidden()
        {
            IReadOnlyList<LayerSelectionFilterRule> rules = LayerFilterRuleSuggester.Suggest(
                new[] { Dwg(new string[0], new[] { "A-WALL" }) },
                null);

            Assert.Empty(rules);
        }

        private static DwgLayerVisibility Dwg(string[] hidden, string[] visible)
        {
            return new DwgLayerVisibility("Drawing.dwg", hidden, visible);
        }

        private static void AssertSelectsOnlyHidden(
            IReadOnlyList<LayerSelectionFilterRule> rules,
            IEnumerable<string> hidden,
            IEnumerable<string> visible)
        {
            Assert.All(hidden, name => Assert.True(LayerSelectionFilterMatcher.IsMatch(name, rules), name));
            Assert.All(visible, name => Assert.False(LayerSelectionFilterMatcher.IsMatch(name, rules), name));
        }
    }
}

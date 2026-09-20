using CAD_Manager.Core;
using System.Collections.Generic;
using Xunit;

namespace CAD_Manager.Core.Tests
{
    public sealed class LayerSelectionFilterMatcherTests
    {
        [Theory]
        [InlineData(LayerNameMatchType.Equals, "A-WALL", "a-wall", true)]
        [InlineData(LayerNameMatchType.Equals, "A-WALL-EXT", "A-WALL", false)]
        [InlineData(LayerNameMatchType.Contains, "A-WALL-EXT", "wall", true)]
        [InlineData(LayerNameMatchType.StartsWith, "A-WALL-EXT", "a-wall", true)]
        [InlineData(LayerNameMatchType.EndsWith, "A-WALL-EXT", "ext", true)]
        public void IsMatch_UsesExpectedCaseInsensitiveOperator(
            LayerNameMatchType matchType,
            string layerName,
            string pattern,
            bool expected)
        {
            List<LayerSelectionFilterRule> rules = new List<LayerSelectionFilterRule>
            {
                new LayerSelectionFilterRule
                {
                    IsEnabled = true,
                    MatchType = matchType,
                    Pattern = pattern
                }
            };

            Assert.Equal(expected, LayerSelectionFilterMatcher.IsMatch(layerName, rules));
        }

        [Fact]
        public void IsMatch_ReturnsTrueWhenAnyEnabledRuleMatches()
        {
            List<LayerSelectionFilterRule> rules = new List<LayerSelectionFilterRule>
            {
                new LayerSelectionFilterRule
                {
                    IsEnabled = false,
                    MatchType = LayerNameMatchType.Equals,
                    Pattern = "A-WALL"
                },
                new LayerSelectionFilterRule
                {
                    IsEnabled = true,
                    MatchType = LayerNameMatchType.EndsWith,
                    Pattern = "-EXT"
                }
            };

            Assert.True(LayerSelectionFilterMatcher.IsMatch("A-WALL-EXT", rules));
        }

        [Fact]
        public void IsMatch_IgnoresEmptyAndDisabledRules()
        {
            List<LayerSelectionFilterRule> rules = new List<LayerSelectionFilterRule>
            {
                new LayerSelectionFilterRule { IsEnabled = true, Pattern = "   " },
                new LayerSelectionFilterRule { IsEnabled = false, Pattern = "WALL" }
            };

            Assert.False(LayerSelectionFilterMatcher.IsMatch("A-WALL", rules));
        }
    }
}

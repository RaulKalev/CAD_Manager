using System;
using System.Collections.Generic;
using System.Linq;

namespace CAD_Manager.Core
{
    /// <summary>
    /// Layer visibility for one DWG in the active view.
    /// </summary>
    public sealed class DwgLayerVisibility
    {
        public DwgLayerVisibility(
            string dwgName,
            IEnumerable<string> hiddenLayers,
            IEnumerable<string> visibleLayers)
        {
            DwgName = dwgName ?? string.Empty;
            HiddenLayers = (hiddenLayers ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList()
                .AsReadOnly();
            VisibleLayers = (visibleLayers ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList()
                .AsReadOnly();
        }

        public string DwgName { get; }

        public IReadOnlyList<string> HiddenLayers { get; }

        public IReadOnlyList<string> VisibleLayers { get; }
    }

    /// <summary>
    /// Proposes reusable layer selection rules from the layers hidden in a view.
    /// </summary>
    public static class LayerFilterRuleSuggester
    {
        private const int MinimumPatternLength = 3;
        private const int MinimumNoiseTokenLength = 3;
        private const int MinimumNoiseTokenCount = 3;
        private const double NoiseTokenShare = 0.6;
        private const int MaximumNoiseTokensPerSide = 3;

        private static readonly char[] Separators = { '-', '_', ' ', '.', '|', '$', '+', '/', '\\', ',', ';', ':' };

        /// <summary>
        /// Returns enabled proposals for hidden layers that no existing rule
        /// already matches. Proposals never match a visible layer by name.
        /// </summary>
        public static IReadOnlyList<LayerSelectionFilterRule> Suggest(
            IEnumerable<DwgLayerVisibility> dwgs,
            IEnumerable<LayerSelectionFilterRule> existingRules)
        {
            List<DwgLayerVisibility> dwgList = (dwgs ?? Enumerable.Empty<DwgLayerVisibility>())
                .Where(dwg => dwg != null)
                .ToList();

            List<string> visibleNames = dwgList
                .SelectMany(dwg => dwg.VisibleLayers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // A disabled rule is still the user's decision about those layers,
            // so treat every non-empty rule as coverage when avoiding repeats.
            List<LayerSelectionFilterRule> coverageRules = (existingRules ?? Enumerable.Empty<LayerSelectionFilterRule>())
                .Where(rule => rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
                .Select(rule => new LayerSelectionFilterRule
                {
                    IsEnabled = true,
                    MatchType = rule.MatchType,
                    Pattern = rule.Pattern
                })
                .ToList();

            Dictionary<string, HiddenLayer> hiddenByName =
                new Dictionary<string, HiddenLayer>(StringComparer.OrdinalIgnoreCase);
            foreach (DwgLayerVisibility dwg in dwgList)
            {
                List<string> dwgNames = dwg.HiddenLayers.Concat(dwg.VisibleLayers).ToList();
                NoiseTokens noise = FindNoiseTokens(dwgNames);

                foreach (string hiddenName in dwg.HiddenLayers)
                {
                    string name = hiddenName.Trim();
                    if (hiddenByName.ContainsKey(name) ||
                        visibleNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                        LayerSelectionFilterMatcher.IsMatch(name, coverageRules))
                    {
                        continue;
                    }

                    hiddenByName[name] = new HiddenLayer(name, GetCoreTokens(name, noise));
                }
            }

            List<HiddenLayer> hiddenLayers = hiddenByName.Values.ToList();
            List<LayerSelectionFilterRule> proposals = new List<LayerSelectionFilterRule>();
            HashSet<HiddenLayer> covered = new HashSet<HiddenLayer>();

            AddSharedPrefixProposals(hiddenLayers, visibleNames, covered, proposals);

            foreach (HiddenLayer layer in hiddenLayers)
            {
                if (covered.Contains(layer))
                    continue;

                string core = layer.Core;
                if (IsSafeContainsPattern(core, visibleNames))
                    proposals.Add(CreateRule(LayerNameMatchType.Contains, core));
                else if (IsSafeContainsPattern(layer.Name, visibleNames))
                    proposals.Add(CreateRule(LayerNameMatchType.Contains, layer.Name));
                else
                    proposals.Add(CreateRule(LayerNameMatchType.Equals, layer.Name));

                covered.Add(layer);
            }

            return RemoveRedundantProposals(proposals)
                .OrderBy(rule => rule.Pattern, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Replaces groups such as E-LIGHT-01 and E-LIGHT-02 with E-LIGHT when
        /// that shorter pattern still does not match any visible layer.
        /// </summary>
        private static void AddSharedPrefixProposals(
            List<HiddenLayer> hiddenLayers,
            List<string> visibleNames,
            HashSet<HiddenLayer> covered,
            List<LayerSelectionFilterRule> proposals)
        {
            List<string> candidates = hiddenLayers
                .SelectMany(layer => layer.GetCorePrefixes())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(prefix => IsSafeContainsPattern(prefix, visibleNames))
                .ToList();

            while (true)
            {
                string bestPrefix = null;
                List<HiddenLayer> bestMatches = null;
                foreach (string prefix in candidates)
                {
                    List<HiddenLayer> matches = hiddenLayers
                        .Where(layer => !covered.Contains(layer) &&
                                        layer.Name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    if (matches.Count < 2)
                        continue;

                    bool isBetter = bestMatches == null ||
                                    matches.Count > bestMatches.Count ||
                                    (matches.Count == bestMatches.Count && prefix.Length > bestPrefix.Length);
                    if (isBetter)
                    {
                        bestPrefix = prefix;
                        bestMatches = matches;
                    }
                }

                if (bestPrefix == null)
                    return;

                proposals.Add(CreateRule(LayerNameMatchType.Contains, bestPrefix));
                foreach (HiddenLayer layer in bestMatches)
                    covered.Add(layer);
                candidates.Remove(bestPrefix);
            }
        }

        private static IEnumerable<LayerSelectionFilterRule> RemoveRedundantProposals(
            List<LayerSelectionFilterRule> proposals)
        {
            List<LayerSelectionFilterRule> distinct = proposals
                .GroupBy(rule => rule.MatchType + "\n" + rule.Pattern, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            List<LayerSelectionFilterRule> containsRules = distinct
                .Where(rule => rule.MatchType == LayerNameMatchType.Contains)
                .ToList();

            return distinct.Where(rule => !containsRules.Any(other =>
                !ReferenceEquals(other, rule) &&
                other.Pattern.Length < rule.Pattern.Length &&
                rule.Pattern.IndexOf(other.Pattern, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static bool IsSafeContainsPattern(string pattern, List<string> visibleNames)
        {
            if (string.IsNullOrWhiteSpace(pattern) ||
                pattern.Length < MinimumPatternLength ||
                !pattern.Any(char.IsLetter))
            {
                return false;
            }

            return !visibleNames.Any(name =>
                name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static LayerSelectionFilterRule CreateRule(LayerNameMatchType matchType, string pattern)
        {
            return new LayerSelectionFilterRule
            {
                IsEnabled = true,
                MatchType = matchType,
                Pattern = pattern
            };
        }

        /// <summary>
        /// Finds leading and trailing name fragments shared by most layers in one
        /// DWG, such as a company code or project number.
        /// </summary>
        private static NoiseTokens FindNoiseTokens(List<string> dwgNames)
        {
            List<List<Token>> tokenized = dwgNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => Tokenize(StripExternalReferencePrefix(name)))
                .Where(tokens => tokens.Count > 1)
                .ToList();

            NoiseTokens noise = new NoiseTokens();
            if (tokenized.Count < MinimumNoiseTokenCount)
                return noise;

            for (int depth = 0; depth < MaximumNoiseTokensPerSide; depth++)
            {
                string token = FindSharedToken(tokenized, tokens => tokens.Count > depth + 1 ? tokens[depth].Text : null);
                if (token == null)
                    break;

                noise.Leading.Add(token);
            }

            for (int depth = 0; depth < MaximumNoiseTokensPerSide; depth++)
            {
                string token = FindSharedToken(
                    tokenized,
                    tokens => tokens.Count > depth + 1 ? tokens[tokens.Count - 1 - depth].Text : null);
                if (token == null)
                    break;

                noise.Trailing.Add(token);
            }

            return noise;
        }

        private static string FindSharedToken(
            List<List<Token>> tokenized,
            Func<List<Token>, string> selector)
        {
            var best = tokenized
                .Select(selector)
                .Where(token => token != null)
                .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Token = group.Key, Count = group.Count() })
                .OrderByDescending(group => group.Count)
                .FirstOrDefault();

            if (best == null ||
                best.Token.Length < MinimumNoiseTokenLength ||
                best.Count < MinimumNoiseTokenCount ||
                best.Count < tokenized.Count * NoiseTokenShare)
            {
                return null;
            }

            return best.Token;
        }

        private static List<Token> GetCoreTokens(string name, NoiseTokens noise)
        {
            int offset = name.Length - StripExternalReferencePrefix(name).Length;
            List<Token> tokens = Tokenize(name.Substring(offset))
                .Select(token => new Token(token.Text, token.Start + offset))
                .ToList();

            int first = 0;
            while (first < noise.Leading.Count &&
                   first < tokens.Count - 1 &&
                   string.Equals(tokens[first].Text, noise.Leading[first], StringComparison.OrdinalIgnoreCase))
            {
                first++;
            }

            int last = tokens.Count - 1;
            int trailingIndex = 0;
            while (trailingIndex < noise.Trailing.Count &&
                   last > first &&
                   string.Equals(tokens[last].Text, noise.Trailing[trailingIndex], StringComparison.OrdinalIgnoreCase))
            {
                last--;
                trailingIndex++;
            }

            return tokens.GetRange(first, last - first + 1);
        }

        /// <summary>
        /// Removes Revit's external-reference prefixes, for example
        /// "Site|A-WALL" and "Site$0$A-WALL".
        /// </summary>
        private static string StripExternalReferencePrefix(string name)
        {
            string result = name;
            int pipeIndex = result.LastIndexOf('|');
            if (pipeIndex >= 0 && pipeIndex < result.Length - 1)
                result = result.Substring(pipeIndex + 1);

            int searchFrom = 0;
            while (true)
            {
                int dollar = result.IndexOf('$', searchFrom);
                if (dollar < 0)
                    break;

                int digitEnd = dollar + 1;
                while (digitEnd < result.Length && char.IsDigit(result[digitEnd]))
                    digitEnd++;

                bool isBindPrefix = digitEnd > dollar + 1 &&
                                    digitEnd < result.Length - 1 &&
                                    result[digitEnd] == '$';
                if (isBindPrefix)
                {
                    result = result.Substring(digitEnd + 1);
                    searchFrom = 0;
                }
                else
                {
                    searchFrom = dollar + 1;
                }
            }

            return result;
        }

        private static List<Token> Tokenize(string name)
        {
            List<Token> tokens = new List<Token>();
            int index = 0;
            while (index < name.Length)
            {
                while (index < name.Length && Array.IndexOf(Separators, name[index]) >= 0)
                    index++;

                int start = index;
                while (index < name.Length && Array.IndexOf(Separators, name[index]) < 0)
                    index++;

                if (index > start)
                    tokens.Add(new Token(name.Substring(start, index - start), start));
            }

            return tokens;
        }

        private sealed class Token
        {
            public Token(string text, int start)
            {
                Text = text;
                Start = start;
            }

            public string Text { get; }

            public int Start { get; }

            public int End => Start + Text.Length;
        }

        private sealed class NoiseTokens
        {
            public List<string> Leading { get; } = new List<string>();

            public List<string> Trailing { get; } = new List<string>();
        }

        private sealed class HiddenLayer
        {
            private readonly List<Token> _coreTokens;

            public HiddenLayer(string name, List<Token> coreTokens)
            {
                Name = name;
                _coreTokens = coreTokens;
                Core = coreTokens.Count == 0
                    ? name
                    : name.Substring(coreTokens[0].Start, coreTokens[coreTokens.Count - 1].End - coreTokens[0].Start);
            }

            public string Name { get; }

            public string Core { get; }

            /// <summary>
            /// Multi-token prefixes of the core; a single token is too broad to
            /// share between layers.
            /// </summary>
            public IEnumerable<string> GetCorePrefixes()
            {
                for (int count = 2; count < _coreTokens.Count; count++)
                {
                    int start = _coreTokens[0].Start;
                    yield return Name.Substring(start, _coreTokens[count - 1].End - start);
                }
            }
        }
    }
}

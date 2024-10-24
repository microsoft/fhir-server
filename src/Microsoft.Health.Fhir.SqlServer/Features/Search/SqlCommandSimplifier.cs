// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class SqlCommandSimplifier
    {
        private static Regex s_findCteMatch = new Regex(",cte(\\d+) AS\\r\\n\\s*\\(\\r\\n\\s*SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch \\r\\n\\s*FROM dbo.ReferenceSearchParam refSource\\r\\n\\s*JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\\r\\n\\s*WHERE refSource.SearchParamId = (\\d*)\\r\\n\\s*AND refTarget.IsHistory = 0 \\r\\n\\s*AND refTarget.IsDeleted = 0 \\r\\n\\s*AND refSource.ResourceTypeId IN \\((\\d*)\\)\\r\\n\\s*AND EXISTS \\(SELECT \\* FROM cte(\\d+) WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1");

        private static string s_removeCteMatchBase = "(\\s*,cte<CteNumber> AS\\r\\n\\s*\\(\\r\\n\\s*SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch \\r\\n\\s*FROM dbo.ReferenceSearchParam refSource\\r\\n\\s*JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\\r\\n\\s*WHERE refSource.SearchParamId = <SearchParamId>\\r\\n\\s*AND refTarget.IsHistory = 0 \\r\\n\\s*AND refTarget.IsDeleted = 0 \\r\\n\\s*AND refSource.ResourceTypeId IN \\(<ResourceTypeId>\\)\\r\\n\\s*AND EXISTS \\(SELECT \\* FROM cte<SourceCte> WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1.*\\r\\n\\s*\\)\\r\\n\\s*,cte<CteNextNumber> AS\\r\\n\\s*\\(\\r\\n\\s*SELECT DISTINCT .*T1, Sid1, IsMatch, .* AS IsPartial \\r\\n\\s*FROM cte<CteNumber>\\r\\n\\s*\\))";

        private static string s_removeUnionSegmentMatchBase = "(\\s*UNION ALL\\r\\n\\s*SELECT T1, Sid1, IsMatch, IsPartial\\r\\n\\s*FROM cte<CteNextNumber> WHERE NOT EXISTS \\(SELECT \\* FROM cte\\d+ WHERE cte\\d+.Sid1 = cte<CteNextNumber>.Sid1 AND cte\\d+.T1 = cte<CteNextNumber>.T1\\))";

        private static string s_existsSelectStatement = "SELECT * FROM cte<SourceCte> WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1";

        private static string s_unionExistsSelectStatment = "SELECT * FROM cte<SourceCte> WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 UNION SELECT * FROM cte<OtherSourceCte> WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1";

        internal static void CombineIterativeIncludes(IndentedStringBuilder stringBuilder, ILogger logger)
        {
            var commandText = stringBuilder.ToString();
            try
            {
                var cteParams = GetIncludeCteParams(commandText);

                var index = 0;
                var redundantCtes = new List<(ReferenceSearchInformation, ReferenceSearchInformation)>();
                var unionCanidateCtes = new List<(ReferenceSearchInformation, ReferenceSearchInformation)>();
                foreach (var item in cteParams)
                {
                    for (int i = index + 1; i < cteParams.Count; i++)
                    {
                        if (item.SearchParamId == cteParams[i].SearchParamId && item.ResourceTypeId == cteParams[i].ResourceTypeId)
                        {
                            if (!unionCanidateCtes.Exists((unionCte) =>
                                (unionCte.Item1.CteNumber + 1 == item.SourceCte && unionCte.Item2.CteNumber + 1 == cteParams[i].SourceCte)
                                || (unionCte.Item2.CteNumber + 1 == item.SourceCte && unionCte.Item1.CteNumber + 1 == cteParams[i].SourceCte)))
                            {
                                unionCanidateCtes.Add((item, cteParams[i]));
                            }
                            else
                            {
                                redundantCtes.Add((item, cteParams[i]));
                            }
                        }
                    }

                    index++;
                }

                foreach (var redundantCte in redundantCtes)
                {
                    // Removes the iterate cte and its projection cte, and cleans up the union at the end.
                    commandText = RemoveCtePair(commandText, redundantCte.Item1);
                }

                foreach (var unionCanidate in unionCanidateCtes)
                {
                    // Unions the two ctes together and removes the first one.
                    commandText = UnionCtes(commandText, unionCanidate);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception combining iterative includes.");
                return; // Use the unmodified string
            }

            stringBuilder.Clear();
            stringBuilder.Append(commandText);
        }

        private static List<ReferenceSearchInformation> GetIncludeCteParams(string commandText)
        {
            var ctes = new List<ReferenceSearchInformation>();
            var cteMatches = s_findCteMatch.Matches(commandText);

            foreach (Match match in cteMatches)
            {
                ctes.Add(new ReferenceSearchInformation()
                {
                    SearchParamId = int.Parse(match.Groups[2].Value),
                    ResourceTypeId = int.Parse(match.Groups[3].Value),
                    SourceCte = int.Parse(match.Groups[4].Value),
                    CteNumber = int.Parse(match.Groups[1].Value),
                });
            }

            return ctes;
        }

        private static string RemoveCtePair(string commandText, ReferenceSearchInformation cteInfo)
        {
            var removeCteMatch = s_removeCteMatchBase
                .Replace("<CteNumber>", cteInfo.CteNumber.ToString(), StringComparison.Ordinal)
                .Replace("<SearchParamId>", cteInfo.SearchParamId.ToString(), StringComparison.Ordinal)
                .Replace("<ResourceTypeId>", cteInfo.ResourceTypeId.ToString(), StringComparison.Ordinal)
                .Replace("<SourceCte>", cteInfo.SourceCte.ToString(), StringComparison.Ordinal)
                .Replace("<CteNextNumber>", (cteInfo.CteNumber + 1).ToString(), StringComparison.Ordinal);
            var removeCteRegex = new Regex(removeCteMatch);
            var matches = removeCteRegex.Matches(commandText);

            if (matches.Count > 1)
            {
                throw new ArgumentException("More than one match found for cte to remove");
            }
            else if (matches.Count == 0)
            {
                throw new ArgumentException("No matches found for cte to remove");
            }

            commandText = commandText.Remove(commandText.IndexOf(matches[0].Value, StringComparison.Ordinal), matches[0].Value.Length);

            var removeUnionSegmentMatch = s_removeUnionSegmentMatchBase
                .Replace("<CteNextNumber>", (cteInfo.CteNumber + 1).ToString(), StringComparison.Ordinal);
            var removeUnionSegmentRegex = new Regex(removeUnionSegmentMatch);
            var unionSegmentMatches = removeUnionSegmentRegex.Matches(commandText);

            if (unionSegmentMatches.Count > 1)
            {
                throw new ArgumentException("More than one match found for union segment to remove");
            }
            else if (unionSegmentMatches.Count == 0)
            {
                throw new ArgumentException("No matches found for union segment to remove");
            }

            commandText = commandText.Remove(commandText.IndexOf(unionSegmentMatches[0].Value, StringComparison.Ordinal), unionSegmentMatches[0].Value.Length);

            return commandText;
        }

        private static string UnionCtes(string commandText, (ReferenceSearchInformation can1, ReferenceSearchInformation can2) unionCanidate)
        {
            var unionCteMatch = s_removeCteMatchBase
                .Replace("<CteNumber>", unionCanidate.can2.CteNumber.ToString(), StringComparison.Ordinal)
                .Replace("<SearchParamId>", unionCanidate.can2.SearchParamId.ToString(), StringComparison.Ordinal)
                .Replace("<ResourceTypeId>", unionCanidate.can2.ResourceTypeId.ToString(), StringComparison.Ordinal)
                .Replace("<SourceCte>", unionCanidate.can2.SourceCte.ToString(), StringComparison.Ordinal)
                .Replace("<CteNextNumber>", (unionCanidate.can2.CteNumber + 1).ToString(), StringComparison.Ordinal);
            var unionCteRegex = new Regex(unionCteMatch);
            var matches = unionCteRegex.Matches(commandText);

            if (matches.Count > 1)
            {
                throw new ArgumentException("More than one match found for union cte");
            }
            else if (matches.Count == 0)
            {
                throw new ArgumentException("No matches found for union cte");
            }

            var existsSelectStatement = s_existsSelectStatement.Replace("<SourceCte>", unionCanidate.can2.SourceCte.ToString(), StringComparison.Ordinal);
            var newExistsSelectStatement = s_unionExistsSelectStatment
                .Replace("<SourceCte>", unionCanidate.can2.SourceCte.ToString(), StringComparison.Ordinal)
                .Replace("<OtherSourceCte>", unionCanidate.can1.SourceCte.ToString(), StringComparison.Ordinal);
            var newCte = matches[0].Value.Replace(existsSelectStatement, newExistsSelectStatement, StringComparison.Ordinal);
            commandText = commandText.Replace(matches[0].Value, newCte, StringComparison.Ordinal);

            commandText = RemoveCtePair(commandText, unionCanidate.can1);
            return commandText;
        }

        internal static void RemoveRedundantParameters(IndentedStringBuilder stringBuilder, SqlParameterCollection sqlParameterCollection, ILogger logger)
        {
            var commandText = stringBuilder.ToString();
            if (commandText.Contains("cte", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                commandText = commandText.Replace("DISTINCT ", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (!commandText.Contains(" OR ", StringComparison.OrdinalIgnoreCase))
                {
                    commandText = RemoveRedundantComparisons(commandText, sqlParameterCollection, '>');
                    commandText = RemoveRedundantComparisons(commandText, sqlParameterCollection, '<');
                }
            }
            catch (Exception ex)
            {
                // Something went wrong simplifing the query, return the query unchanged.
                logger.LogWarning(ex, "Exception simplifing SQL query");
                return;
            }

            stringBuilder.Clear();
            stringBuilder.Append(commandText);
        }

        private static string RemoveRedundantComparisons(string commandText, SqlParameterCollection sqlParameterCollection, char operatorChar)
        {
            var operatorMatch = new Regex("(\\w*\\.?\\w+) " + operatorChar + "=? (@p\\d+)");
            var operatorMatches = operatorMatch.Matches(commandText);

            var fieldToParameterComparisons = new Dictionary<string, List<(string, Match)>>();
            foreach (Match match in operatorMatches)
            {
                var groups = match.Groups;
                if (!fieldToParameterComparisons.TryGetValue(groups[1].Value, out List<(string, Match)> value))
                {
                    value = new List<(string, Match)>();
                    fieldToParameterComparisons.Add(groups[1].Value, value);
                }

                value.Add((groups[2].Value, match));
            }

            foreach (string field in fieldToParameterComparisons.Keys)
            {
                long targetValue;
                if (fieldToParameterComparisons[field].Count > 1)
                {
                    targetValue = (long)sqlParameterCollection[fieldToParameterComparisons[field][0].Item1].Value;
                    int targetIndex = 0;
                    for (int index = 1; index < fieldToParameterComparisons[field].Count; index++)
                    {
                        long value = (long)sqlParameterCollection[fieldToParameterComparisons[field][index].Item1].Value;
                        if ((operatorChar == '>' && value > targetValue)
                            || (operatorChar == '<' && value < targetValue)
                            || (value == targetValue
                                && !fieldToParameterComparisons[field][index].Item2.Value.Contains('=', StringComparison.OrdinalIgnoreCase)))
                        {
                            targetValue = value;
                            targetIndex = index;
                        }
                    }

                    for (int index = 0; index < fieldToParameterComparisons[field].Count; index++)
                    {
                        if (index == targetIndex)
                        {
                            continue;
                        }

                        commandText = commandText.Replace(fieldToParameterComparisons[field][index].Item2.Value, "1 = 1", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            return commandText;
        }

        private class ReferenceSearchInformation
        {
            public int CteNumber { get; set; }

            public int SearchParamId { get; set; }

            public int ResourceTypeId { get; set; }

            public int SourceCte { get; set; }
        }
    }
}

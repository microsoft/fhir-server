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
    }
}

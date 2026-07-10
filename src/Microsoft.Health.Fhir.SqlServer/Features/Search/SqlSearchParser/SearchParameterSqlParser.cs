// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SearchParameterSqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly Dictionary<SearchParamType, ISqlParser> _sqlParsers;
        private readonly Dictionary<CompositeType, ISqlParser> _compositeSqlParsers;
        private readonly SystemSqlParser _systemSqlParser;
        private readonly IdSqlParser _idSqlParser;
        private readonly ISqlServerFhirModel _sqlServerFhirModel;
        private readonly IncludeSqlParser _includeSqlParser;
        private readonly RevIncludeSqlParser _revIncludeSqlParser;
        private readonly ChainedSqlParser _chainedSqlParser;
        private readonly ReversedChainSqlParser _reversedChainSqlParser;
        private readonly LastUpdatedSqlParser _lastUpdatedSqlParser;

        public SearchParameterSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel fhirModel)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(fhirModel);

            _parameterCollection = parameterCollection;
            _sqlServerFhirModel = fhirModel;
            _systemSqlParser = new SystemSqlParser();
            _idSqlParser = new IdSqlParser();
            _lastUpdatedSqlParser = new LastUpdatedSqlParser();
            _sqlParsers = new Dictionary<SearchParamType, ISqlParser>()
            {
                { SearchParamType.Number, new NumberSqlParser(parameterCollection) },
                { SearchParamType.Date, new DateTimeSqlParser(parameterCollection) },
                { SearchParamType.String, new StringSqlParser(parameterCollection) },
                { SearchParamType.Token, new TokenSqlParser(parameterCollection) },
                { SearchParamType.Reference, new ReferenceSqlParser(parameterCollection, fhirModel) },
                { SearchParamType.Uri, new UriSqlParser(parameterCollection) },
                { SearchParamType.Quantity, new BaseParsers.QuantitySqlParser(parameterCollection) },
            };
            _compositeSqlParsers = new Dictionary<CompositeType, ISqlParser>()
            {
                { CompositeType.TokenString, new TokenStringCompositeSqlParser(parameterCollection) },
                { CompositeType.TokenToken, new TokenTokenCompositeSqlParser(parameterCollection) },
                { CompositeType.TokenQuantity, new TokenQuantityCompositeSqlParser(parameterCollection) },
                { CompositeType.TokenReference, new ReferenceTokenCompositeSqlParser(parameterCollection, fhirModel) },
                { CompositeType.TokenDate, new TokenDateTimeCompositeSqlParser(parameterCollection) },
                { CompositeType.TokenNumberNumber, new TokenNumberNumberCompositeSqlParser(parameterCollection) },
            };

            _includeSqlParser = new IncludeSqlParser(parameterCollection, fhirModel);
            _revIncludeSqlParser = new RevIncludeSqlParser(parameterCollection, fhirModel);
            _chainedSqlParser = new ChainedSqlParser(parameterCollection, this, fhirModel);
            _reversedChainSqlParser = new ReversedChainSqlParser(parameterCollection, this, fhirModel);
        }

        public string? ParseMultiple(IDictionary<string, IList<string>> parameters, SqlSearchOptions sqlSearchOptions, ContinuationToken? continuationToken = null)
        {
            var parametersCopy = parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var sqlBuilder = new StringBuilder();
            var cteIndex = 0;
            string? lastCteName = null;
            Dictionary<string, IList<string>> includeParameters = new();
            Dictionary<string, IList<string>> chainedParameters = new();
            Dictionary<string, IList<string>> reversedChainedParameters = new();
            var parserOptions = new ParserOptions()
            {
                ContinuationToken = continuationToken,
                Count = sqlSearchOptions.MaxItemCount,
            };

            // Extract and process _sort parameter
            string? sortParameterName = null;
            bool sortDescending = false;
            bool sortIsSpecialParameter = false;

            if (parametersCopy.TryGetValue("_sort", out var sortValues) && sortValues.Count > 0)
            {
                var sortValue = sortValues[0]; // Use first sort parameter
                sortDescending = sortValue.StartsWith('-');
                sortParameterName = sortDescending ? sortValue[1..] : sortValue;

                // Check if this is a special parameter (_lastUpdated or _type)
                sortIsSpecialParameter = sortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase) ||
                                        sortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase);

                parserOptions.SortParameterName = sortParameterName;
                parserOptions.SortDescending = sortDescending;
                parserOptions.SortIsSpecialParameter = sortIsSpecialParameter;
                parserOptions.SortQuerySecondPhase = sqlSearchOptions.SortQuerySecondPhase;

                parametersCopy.Remove("_sort");
            }

            parametersCopy = parametersCopy.Where(param =>
            {
                if (param.Key.Equals("_elements", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // Check for _summary=accurate parameter
            if (parametersCopy.TryGetValue("_summary", out var summaryValues))
            {
                if (summaryValues.Any(v => v.Equals("count", StringComparison.OrdinalIgnoreCase)))
                {
                    parserOptions.IncludeTotalCount = true;
                }

                parametersCopy.Remove("_summary");
            }

            if (parametersCopy.TryGetValue("_type", out var typeValues))
            {
                foreach (var typeValue in typeValues.SelectMany(types => types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(_sqlServerFhirModel.GetResourceTypeId))
                {
                    parserOptions.ResourceTypes.Add(typeValue);
                }

                parametersCopy.Remove("_type");
            }

            if (parametersCopy.TryGetValue("_type:not", out var excludedTypeValues))
            {
                foreach (var typeValue in excludedTypeValues.SelectMany(types => types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(_sqlServerFhirModel.GetResourceTypeId))
                {
                    parserOptions.ExcludedResourceTypes.Add(typeValue);
                }

                parametersCopy.Remove("_type:not");
            }

            parametersCopy.Remove("_count");
            parametersCopy.Remove("_total");
            parametersCopy.Remove("ct");
            parametersCopy.Remove(KnownQueryParameterNames.IncludesContinuationToken);
            parametersCopy.Remove(KnownQueryParameterNames.IncludesCount);

            sqlBuilder.AppendLine("DECLARE @FilteredData AS TABLE (ResourceTypeId smallint, ResourceSurrogateId bigint, IsMatch bit, IsPartial bit, Row int)");
            sqlBuilder.AppendLine(";WITH");

            // *********************************************************************** Basic Search Parameters ***********************************************************************

            // If no search parameters, use SystemSqlParser for basic resource retrieval
            if (parametersCopy.Count == 0)
            {
                lastCteName = $"cte{cteIndex}";
                cteIndex++;

                sqlBuilder.AppendLine($"{lastCteName} AS (");

                sqlBuilder.Append(_systemSqlParser.Parse(string.Empty, string.Empty, parserOptions));

                sqlBuilder.AppendLine();
                sqlBuilder.Append(')');
            }
            else
            {
                foreach (var kvp in parametersCopy)
                {
                    if (kvp.Key.StartsWith("_include", StringComparison.OrdinalIgnoreCase) || kvp.Key.StartsWith("_revinclude", StringComparison.OrdinalIgnoreCase))
                    {
                        includeParameters.Add(kvp.Key, kvp.Value);
                        continue;
                    }

                    if (kvp.Key.StartsWith("_has:", StringComparison.OrdinalIgnoreCase))
                    {
                        reversedChainedParameters.Add(kvp.Key, kvp.Value);
                        continue;
                    }

                    if (kvp.Key.Contains('.', StringComparison.OrdinalIgnoreCase))
                    {
                        chainedParameters.Add(kvp.Key, kvp.Value);
                        continue;
                    }

                    foreach (var value in kvp.Value)
                    {
                        var parameter = _parameterCollection.GetByCode(kvp.Key, parserOptions.ResourceTypes.FirstOrDefault());
                        if (parameter == null)
                        {
                            continue;
                        }

                        var cteName = $"cte{cteIndex}";
                        parserOptions.CteName = cteName;

                        if (cteIndex > 0)
                        {
                            sqlBuilder.Append(',');
                        }

                        sqlBuilder.AppendLine($"{cteName} AS (");

                        sqlBuilder.Append(Parse(kvp.Key, value, parserOptions));

                        sqlBuilder.AppendLine();
                        sqlBuilder.Append(')');

                        lastCteName = cteName;
                        parserOptions.LastCteName = lastCteName;

                        cteIndex++;
                    }
                }
            }

            // *********************************************************************** Chained Search Parameters ***********************************************************************
            if (chainedParameters.Count > 0)
            {
                var cteName = string.Empty;

                foreach (var kvp in chainedParameters)
                {
                    foreach (var value in kvp.Value)
                    {
                        cteName = $"cte{cteIndex}";
                        parserOptions.CteName = cteName;

                        sqlBuilder.Append(_chainedSqlParser.Parse(kvp.Key, value, parserOptions));

                        lastCteName = cteName;
                        parserOptions.LastCteName = lastCteName;
                        cteIndex++;
                    }
                }
            }

            // *********************************************************************** Reversed Chained Search Parameters ***********************************************************************
            if (reversedChainedParameters.Count > 0)
            {
                var cteName = string.Empty;

                foreach (var kvp in reversedChainedParameters)
                {
                    foreach (var value in kvp.Value)
                    {
                        cteName = $"cte{cteIndex}";
                        parserOptions.CteName = cteName;

                        sqlBuilder.Append(_reversedChainSqlParser.Parse(kvp.Key, value, parserOptions));

                        lastCteName = cteName;
                        parserOptions.LastCteName = lastCteName;
                        cteIndex++;
                    }
                }
            }

            if (lastCteName == null)
            {
                return null;
            }

            if (!parserOptions.IncludeTotalCount)
            {
                var cteName = $"cte{cteIndex}";
                parserOptions.CteName = cteName;
                cteIndex++;

                sqlBuilder.AppendLine($",{cteName} AS (")
                    .AppendLine($"SELECT TOP {parserOptions.Count + 1} * FROM {lastCteName} r")
                    .AppendLine($"  ORDER BY r.ResourceTypeId {(parserOptions.SortDescending ? "DESC" : "ASC")}, r.ResourceSurrogateId {(parserOptions.SortDescending ? "DESC" : "ASC")}")
                    .AppendLine(")");

                lastCteName = cteName;
                parserOptions.LastCteName = lastCteName;
            }

            // *********************************************************************** Include Parameters ***********************************************************************
            if (includeParameters.Count > 0 && !parserOptions.IncludeTotalCount)
            {
                var baseCteName = $"cte{cteIndex}";
                cteIndex++;

                sqlBuilder.AppendLine($"INSERT INTO @FilteredData SELECT ResourceTypeId, ResourceSurrogateId, IsMatch = 1, IsPartial = 0, Row FROM {lastCteName}");
                sqlBuilder.AppendLine($"; WITH {baseCteName} AS(SELECT * FROM @FilteredData)");

                parserOptions.LastCteName = baseCteName;

                var includeCteNames = new List<string> { baseCteName };

                // Order include parameters so iterate includes come after their dependencies
                var orderedIncludes = OrderIncludeParameters(includeParameters);

                // Process each ordered include
                for (int i = 0; i < orderedIncludes.Count; i++)
                {
                    var orderedInclude = orderedIncludes[i];

                    // Determine the LastCteName for this include
                    string includeLastCteName;

                    if (orderedInclude.IsIterate && orderedInclude.DependsOnIndices.Count > 0)
                    {
                        // Create a union CTE of all dependency CTEs
                        var unionCteName = $"cte{cteIndex}";
                        cteIndex++;

                        var dependencyCteNames = new List<string>();
                        foreach (var depIndex in orderedInclude.DependsOnIndices)
                        {
                            // Find the dependency in the ordered list by searching for the index
                            var dependency = orderedIncludes.FirstOrDefault(inc => inc.OriginalIndex == depIndex);

                            if (dependency != null && dependency.CteNames.Count > 0)
                            {
                                dependencyCteNames.AddRange(dependency.CteNames);
                            }
                        }

                        if (dependencyCteNames.Count > 0)
                        {
                            // Create union CTE
                            ParserUtil.AddUnionCte(sqlBuilder, unionCteName, dependencyCteNames);
                            includeLastCteName = unionCteName;
                        }
                        else
                        {
                            // No dependencies found, fall back to base CTE
                            includeLastCteName = baseCteName;
                        }
                    }
                    else
                    {
                        // Regular include uses the base CTE
                        includeLastCteName = baseCteName;
                    }

                    // Process each value in this include
                    var includeCteName = $"cte{cteIndex}";
                    parserOptions.CteName = includeCteName;
                    parserOptions.LastCteName = includeLastCteName;
                    cteIndex++;

                    sqlBuilder.AppendLine($",{includeCteName} AS (");

                    // Choose the appropriate parser based on whether this is _include or _revinclude
                    ISqlParser parser = orderedInclude.ParameterName.StartsWith("_revinclude", StringComparison.OrdinalIgnoreCase)
                        ? _revIncludeSqlParser
                        : _includeSqlParser;

                    var includeSql = parser.Parse(orderedInclude.ParameterName, orderedInclude.Value, parserOptions);
                    sqlBuilder.Append(includeSql);
                    sqlBuilder.AppendLine();
                    sqlBuilder.Append(')');

                    includeCteNames.Add(includeCteName);
                    orderedInclude.CteNames.Add(includeCteName);
                }

                sqlBuilder.AppendLine();

                var unionCte = $"cte{cteIndex}";
                cteIndex++;

                ParserUtil.AddUnionCte(sqlBuilder, unionCte, includeCteNames);

                lastCteName = unionCte;
                cteIndex++;
            }

            sqlBuilder.AppendLine();

            // *********************************************************************** Get Resources ***********************************************************************
            // If this is a count query, return count instead of full results
            if (parserOptions.IncludeTotalCount)
            {
                sqlBuilder.AppendLine($"SELECT COUNT_BIG(*) AS Total FROM {lastCteName}");
            }
            else
            {
                // Build the ORDER BY clause based on sort parameters
                string orderByClause;

                if (!string.IsNullOrEmpty(sortParameterName))
                {
                    if (sortIsSpecialParameter)
                    {
                        // Special parameters map directly to Resource table columns
                        if (sortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        {
                            // _lastUpdated maps to ResourceSurrogateId (which encodes timestamp)
                            orderByClause = sortDescending
                                ? "ORDER BY t.IsMatch DESC, t.ResourceSurrogateId DESC"
                                : "ORDER BY t.IsMatch DESC, t.ResourceSurrogateId ASC";
                        }
                        else if (sortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase))
                        {
                            // _type maps to ResourceTypeId
                            orderByClause = sortDescending
                                ? "ORDER BY t.IsMatch DESC, t.ResourceTypeId DESC, t.ResourceSurrogateId DESC"
                                : "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                        }
                        else
                        {
                            // Fallback to default ordering
                            orderByClause = "ORDER BY t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                        }
                    }
                    else
                    {
                        // Regular search parameters - use two-phase approach
                        // Phase 1 (when SortQuerySecondPhase = false): Resources WITHOUT the sort parameter
                        // Phase 2 (when SortQuerySecondPhase = true): Resources WITH the sort parameter

                        if (sortDescending)
                        {
                            // Descending: first show resources WITH values (phase 2), then WITHOUT (phase 1)
                            if (sqlSearchOptions.SortQuerySecondPhase)
                            {
                                // Phase 2: Resources with values, sorted descending by IsMatch and then by value
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId DESC";
                            }
                            else
                            {
                                // Phase 1: Resources without values (missing the search parameter)
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId DESC";
                            }
                        }
                        else
                        {
                            // Ascending: first show resources WITHOUT values (phase 1), then WITH (phase 2)
                            if (sqlSearchOptions.SortQuerySecondPhase)
                            {
                                // Phase 2: Resources with values, sorted ascending by IsMatch and then by value
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                            }
                            else
                            {
                                // Phase 1: Resources without values (missing the search parameter)
                                orderByClause = "ORDER BY t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                            }
                        }
                    }
                }
                else
                {
                    // No sort parameter - use default ordering
                    orderByClause = "ORDER BY t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                }

                sqlBuilder.AppendLine($"SELECT * FROM (")
                    .AppendLine("SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource ")
                    .AppendLine("FROM dbo.Resource AS r ")
                    .AppendLine($"JOIN {lastCteName} AS f ON r.ResourceTypeId = f.ResourceTypeId AND r.ResourceSurrogateId = f.ResourceSurrogateId ")
                    .AppendLine("WHERE r.IsHistory = 0 AND r.IsDeleted = 0 ")
                    .AppendLine($") AS t {orderByClause}");
            }

            return sqlBuilder.ToString();
        }

        public ISqlParser GetParser(string name, short resourceTypeId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (name.StartsWith(KnownQueryParameterNames.Id, StringComparison.OrdinalIgnoreCase))
            {
                return _idSqlParser;
            }

            if (name.StartsWith(KnownQueryParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
            {
                return _lastUpdatedSqlParser;
            }

            if (name.Contains(KnownQueryParameterNames.ReverseChain, StringComparison.OrdinalIgnoreCase))
            {
                return _reversedChainSqlParser;
            }

            if (name.Contains('.', StringComparison.OrdinalIgnoreCase))
            {
                return _chainedSqlParser;
            }

            var parameter = _parameterCollection.GetByCode(name, resourceTypeId);
            if (parameter == null)
            {
                throw new ArgumentException($"Search parameter '{name}' is not supported for resource type '{resourceTypeId}'.");
            }

            ISqlParser? parser = null;
            if (parameter.SearchParameterInfo.Type == SearchParamType.Composite)
            {
                var compositeType = BaseCompositeSqlParser.DetermineCompositeType(parameter.SearchParameterInfo, _parameterCollection);
                if (!_compositeSqlParsers.TryGetValue(compositeType, out parser))
                {
                    throw new ArgumentException($"Parser not found for composite type '{compositeType}'.");
                }
            }
            else if (!_sqlParsers.TryGetValue(parameter.SearchParameterInfo.Type, out parser))
            {
                throw new ArgumentException($"Parser not found for search parameter type '{parameter.SearchParameterInfo.Type}'.");
            }

            return parser;
        }

        /// <summary>
        /// Builds the SQL query for the given search parameters and options.
        /// </summary>
        /// <param name="name">The name of the search parameter.</param>
        /// <param name="value">The value of the search parameter.</param>
        /// <param name="options">The parser options.</param>
        /// <returns>The parsed SQL query.</returns>
        /// <exception cref="ArgumentNullException">If the name is null or whitespace.</exception>
        /// <exception cref="ArgumentException">If the search parameter is not supported or the parser is not found.</exception>
        private string Parse(string name, string value, ParserOptions options)
        {
            var parser = GetParser(name, options.ResourceTypes.FirstOrDefault());
            if (parser == null)
            {
                throw new ArgumentException($"Parser not found for search parameter '{name}'.");
            }

            return parser.Parse(name, value, options) ?? string.Empty;
        }

        /// <summary>
        /// Orders include parameters so that _include:iterate parameters are processed after
        /// all _include parameters that produce the resources they depend on.
        /// </summary>
        /// <param name="includeParameters">Dictionary of include parameter names to their values.</param>
        /// <returns>An ordered list of include parameters with their dependency information.</returns>
        private List<OrderedInclude> OrderIncludeParameters(Dictionary<string, IList<string>> includeParameters)
        {
            var allIncludes = new List<OrderedInclude>();

            // Convert all includes to OrderedInclude objects
            int index = 0;
            foreach (var kvp in includeParameters)
            {
                foreach (var value in kvp.Value)
                {
                    var orderedInclude = new OrderedInclude
                    {
                        OriginalIndex = index,
                        ParameterName = kvp.Key,
                        Value = value,
                        IsIterate = kvp.Key.Contains(":iterate", StringComparison.OrdinalIgnoreCase) || kvp.Key.Contains(":recurse", StringComparison.OrdinalIgnoreCase),
                    };
                    allIncludes.Add(orderedInclude);
                    index++;
                }
            }

            // Build dependency graph
            for (int i = 0; i < allIncludes.Count; i++)
            {
                if (!allIncludes[i].IsIterate)
                {
                    continue; // Regular includes have no dependencies
                }

                // For each iterate include, find all includes it depends on
                var iterateInclude = allIncludes[i];
                var requiredSourceTypes = new HashSet<short>();

                // Get all source resource types this iterate include needs
                var parts = iterateInclude.Value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    try
                    {
                        var sourceResourceTypeId = _sqlServerFhirModel.GetResourceTypeId(parts[0]);
                        requiredSourceTypes.Add(sourceResourceTypeId);
                    }
                    catch
                    {
                        // Invalid resource type, skip
                    }
                }

                // Check all other includes to see if they produce any of the required source types
                for (int j = 0; j < allIncludes.Count; j++)
                {
                    if (i == j)
                    {
                        continue; // Don't depend on self
                    }

                    var potentialDependency = allIncludes[j];
                    var producedTypes = new HashSet<short>();

                    // Get all resource types this include could produce
                    var targetTypes = GetIncludeTargetResourceTypes(potentialDependency.Value);
                    foreach (var targetType in targetTypes)
                    {
                        producedTypes.Add(targetType);
                    }

                    // If this include produces any of the required source types, add it as a dependency
                    if (requiredSourceTypes.Overlaps(producedTypes))
                    {
                        iterateInclude.DependsOnIndices.Add(j);
                    }
                }
            }

            // Topological sort to order includes respecting dependencies
            var result = new List<OrderedInclude>();
            var processed = new HashSet<int>();
            var processing = new HashSet<int>();

            bool TopologicalSort(int index)
            {
                if (processed.Contains(index))
                {
                    return true; // Already processed
                }

                if (processing.Contains(index))
                {
                    // Circular dependency detected - this shouldn't happen with proper iterate semantics
                    // but handle gracefully by breaking the cycle
                    return false;
                }

                processing.Add(index);

                // Process all dependencies first
                foreach (var dependencyIndex in allIncludes[index].DependsOnIndices)
                {
                    if (!TopologicalSort(dependencyIndex))
                    {
                        // Circular dependency, skip this dependency
                        continue;
                    }
                }

                processing.Remove(index);
                processed.Add(index);

                // Add this include to result
                result.Add(allIncludes[index]);

                return true;
            }

            // Process all includes
            for (int i = 0; i < allIncludes.Count; i++)
            {
                TopologicalSort(i);
            }

            return result;
        }

        /// <summary>
        /// Extracts the target resource types from an include parameter value.
        /// </summary>
        /// <param name="includeValue">The include parameter value (e.g., "Patient:organization" or "Observation:subject:Patient").</param>
        /// <returns>A list of target resource type IDs.</returns>
        private List<short> GetIncludeTargetResourceTypes(string includeValue)
        {
            var targetTypes = new List<short>();

            // Parse the include value: ResourceType:searchParam or ResourceType:searchParam:targetType
            var parts = includeValue.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return targetTypes;
            }

            var sourceResourceType = parts[0];
            short sourceResourceTypeId;

            try
            {
                sourceResourceTypeId = _sqlServerFhirModel.GetResourceTypeId(sourceResourceType);
            }
            catch
            {
                return targetTypes;
            }

            // If explicit target type is specified (3 parts)
            if (parts.Length >= 3)
            {
                try
                {
                    var targetTypeId = _sqlServerFhirModel.GetResourceTypeId(parts[2]);
                    targetTypes.Add(targetTypeId);
                    return targetTypes;
                }
                catch
                {
                    // Invalid target type, continue to infer from search parameter
                }
            }

            if (parts.Length >= 2)
            {
                IList<SearchParameterIdWrapper> parameters = new List<SearchParameterIdWrapper>();

                if (parts[1] == "*")
                {
                    parameters = _parameterCollection.GetByResourceType(parts[0]);
                }
                else
                {
                    parameters.Add(_parameterCollection.GetByCode(parts[1], sourceResourceTypeId));
                }

                foreach (var parameter in parameters)
                {
                    if (parameter != null && parameter.SearchParameterInfo.Type == SearchParamType.Reference)
                    {
                        foreach (var targetResourceType in parameter.SearchParameterInfo.TargetResourceTypes)
                        {
                            try
                            {
                                var targetTypeId = _sqlServerFhirModel.GetResourceTypeId(targetResourceType);
                                targetTypes.Add(targetTypeId);
                            }
                            catch
                            {
                                // Skip invalid resource types
                            }
                        }
                    }
                }
            }

            return targetTypes;
        }

        /// <summary>
        /// Represents an ordered include parameter with its dependencies.
        /// </summary>
        private class OrderedInclude
        {
            public int OriginalIndex { get; set; }

            public string ParameterName { get; set; } = string.Empty;

            public string Value { get; set; } = string.Empty;

            public bool IsIterate { get; set; }

            public HashSet<int> DependsOnIndices { get; set; } = new HashSet<int>();

            public List<string> CteNames { get; set; } = new List<string>();
        }
    }
}

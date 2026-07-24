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
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
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
        private readonly SortSqlParser _sortSqlParser;
        private readonly NotReferencedSqlParser _notReferencedSqlParser;
        private readonly CompartmentSqlParser _compartmentSqlParser;
        private readonly ILogger<SearchParameterSqlParser> _logger;

        public SearchParameterSqlParser(SqlSearchParameterDefinitionManager parameterCollection, ISqlServerFhirModel fhirModel, ICompartmentDefinitionManager compartmentDefinitionManager, ILogger<SearchParameterSqlParser> logger)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(fhirModel);
            ArgumentNullException.ThrowIfNull(compartmentDefinitionManager);
            ArgumentNullException.ThrowIfNull(logger);

            _parameterCollection = parameterCollection;
            _logger = logger;
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
            _sortSqlParser = new SortSqlParser(parameterCollection);
            _notReferencedSqlParser = new NotReferencedSqlParser(parameterCollection, fhirModel);
            _compartmentSqlParser = new CompartmentSqlParser(fhirModel, parameterCollection, compartmentDefinitionManager);
        }

        public string? ParseMultiple(IDictionary<string, IList<string>> parameters, SqlSearchOptions sqlSearchOptions, ContinuationToken? continuationToken = null, IncludesContinuationToken? includesContinuationToken = null)
        {
            var parametersCopy = DeepCopyParameters(parameters);

            var cteIndex = 0;
            string? lastCteName = null;
            Dictionary<string, IList<string>> includeParameters = new();
            Dictionary<string, IList<string>> chainedParameters = new();
            Dictionary<string, IList<string>> reversedChainedParameters = new();
            Dictionary<string, IList<string>> notReferencedParameters = new();
            var parserOptions = new ParserOptions()
            {
                ContinuationToken = continuationToken,
                IncludesContinuationToken = includesContinuationToken,
                Count = sqlSearchOptions.MaxItemCount,
                IncludeCount = sqlSearchOptions.IncludeCount,
                GetTotalCount = sqlSearchOptions.CountOnly,
            };
            var sqlBuilder = parserOptions.SqlQueryBuilder;

            if (continuationToken != null)
            {
                _logger.LogInformation("Parsing continuation token {ContinuationToken}", continuationToken);
            }

            // Extract and process _sort parameter
            string? sortParameterName = null;
            bool sortDescending = false;
            bool sortIsSpecialParameter = false;

            if (parametersCopy.TryGetValue("_sort", out var sortValues) && sortValues.Count > 0 && !parserOptions.GetTotalCount)
            {
                var sortValue = sortValues[0]; // Use first sort parameter
                sortDescending = sortValue.StartsWith('-');
                sortParameterName = sortDescending ? sortValue[1..] : sortValue;

                // Check if this is a special parameter (_lastUpdated or _type)
                sortIsSpecialParameter = sortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase) ||
                                        sortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase);

                if ((sqlSearchOptions.SortQuerySecondPhase && sortDescending)
                    || (!sqlSearchOptions.SortQuerySecondPhase && !sortDescending && !sortIsSpecialParameter && !sqlSearchOptions.IsSortWithFilter && !sqlSearchOptions.SortHasMissingModifier))
                {
                    if (!parametersCopy.TryAdd(sortParameterName + ":missing", new List<string> { "true" }))
                    {
                        parametersCopy[sortParameterName + ":missing"].Add("true");
                    }
                }
                else
                {
                    parserOptions.SortParameterName = sortParameterName;
                    parserOptions.SortDescending = sortDescending;
                    parserOptions.SortIsSpecialParameter = sortIsSpecialParameter;

                    if (parserOptions.ContinuationToken != null && !sortIsSpecialParameter)
                    {
                        // For non-special sort, extract sort value and use it in the sort CTE
                        parserOptions.SortContinuationToken = parserOptions.ContinuationToken.SortValue;
                        parserOptions.SortContinuationResourceSurrogateId = parserOptions.ContinuationToken.ResourceSurrogateId;
                        parserOptions.ContinuationToken = null;
                    }
                }
            }

            parametersCopy.Remove("_sort");

            // Check for _summary=accurate parameter
            if (parametersCopy.TryGetValue("_summary", out var summaryValues))
            {
                if (summaryValues.Any(v => v.Equals("count", StringComparison.OrdinalIgnoreCase)))
                {
                    parserOptions.GetTotalCount = true;
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

            parametersCopy.Remove("_elements");
            parametersCopy.Remove("_count");
            parametersCopy.Remove("_total");
            parametersCopy.Remove("ct");
            parametersCopy.Remove(KnownQueryParameterNames.IncludesContinuationToken);
            parametersCopy.Remove(KnownQueryParameterNames.IncludesCount);

            // Extract compartment parameters
            string? compartmentType = null;
            string? compartmentId = null;
            if (parametersCopy.TryGetValue("_compartmentType", out var compartmentTypeValues))
            {
                compartmentType = compartmentTypeValues.FirstOrDefault();
                parametersCopy.Remove("_compartmentType");
            }

            if (parametersCopy.TryGetValue("_compartmentId", out var compartmentIdValues))
            {
                compartmentId = compartmentIdValues.FirstOrDefault();
                parametersCopy.Remove("_compartmentId");
            }

            // *********************************************************************** Basic Search Parameters ***********************************************************************

            // If compartment search is specified, use it as the base CTE
            if (!string.IsNullOrEmpty(compartmentType) && !string.IsNullOrEmpty(compartmentId))
            {
                parserOptions.CteNumber = cteIndex;
                _compartmentSqlParser.Parse(compartmentType, compartmentId, parserOptions);
                lastCteName = $"cte{cteIndex}";
                parserOptions.LastCteName = lastCteName;
                cteIndex++;
            }

            // If no search parameters, use SystemSqlParser for basic resource retrieval (only if no compartment was already set)
            if (parametersCopy.Count == 0 && lastCteName == null)
            {
                parserOptions.CteNumber = cteIndex;
                _systemSqlParser.Parse(string.Empty, string.Empty, parserOptions);
                lastCteName = $"cte{cteIndex}";
                cteIndex++;
            }
            else if (parametersCopy.Count > 0)
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

                    if (string.Equals(kvp.Key, KnownQueryParameterNames.NotReferenced, StringComparison.OrdinalIgnoreCase))
                    {
                        notReferencedParameters.Add(kvp.Key, kvp.Value);
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
                        parserOptions.CteNumber = cteIndex;

                        Parse(kvp.Key, value, parserOptions);

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
                        parserOptions.CteNumber = cteIndex;

                        _chainedSqlParser.Parse(kvp.Key, value, parserOptions);

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
                        parserOptions.CteNumber = cteIndex;

                        _reversedChainSqlParser.Parse(kvp.Key, value, parserOptions);

                        lastCteName = cteName;
                        parserOptions.LastCteName = lastCteName;
                        cteIndex++;
                    }
                }
            }

            if (lastCteName == null)
            {
                // No search CTEs generated (e.g., only _include/_revinclude/_not-referenced/chained params) - generate base system CTE
                if (includeParameters.Count > 0 || reversedChainedParameters.Count > 0 || notReferencedParameters.Count > 0 || chainedParameters.Count > 0)
                {
                    parserOptions.CteNumber = cteIndex;
                    _systemSqlParser.Parse(string.Empty, string.Empty, parserOptions);
                    lastCteName = $"cte{cteIndex}";
                    cteIndex++;
                }
                else
                {
                    return null;
                }
            }

            // *********************************************************************** Not Referenced Parameters ***********************************************************************
            if (notReferencedParameters.Count > 0)
            {
                foreach (var kvp in notReferencedParameters)
                {
                    foreach (var value in kvp.Value)
                    {
                        // Skip invalid values (no colon separator)
                        if (!value.Contains(':', StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var cteName = $"cte{cteIndex}";
                        parserOptions.CteNumber = cteIndex;
                        parserOptions.LastCteName = lastCteName;

                        _notReferencedSqlParser.Parse(kvp.Key, value, parserOptions);

                        lastCteName = cteName;
                        parserOptions.LastCteName = lastCteName;
                        cteIndex++;
                    }
                }
            }

            // *********************************************************************** Apply Sort (if needed) ***********************************************************************
            // Apply sorting AFTER getting initial results but BEFORE includes
            bool hasSortCte = false;
            if (!string.IsNullOrEmpty(parserOptions.SortParameterName) && !parserOptions.SortIsSpecialParameter && !parserOptions.GetTotalCount)
            {
                var sortCteName = $"cte{cteIndex}";
                cteIndex++;

                var sortCte = _sortSqlParser.CreateSortCte(
                    parserOptions.SortParameterName,
                    parserOptions.SortDescending,
                    lastCteName!,
                    sortCteName,
                    parserOptions.ResourceTypes.FirstOrDefault(),
                    parserOptions.SortContinuationToken,
                    parserOptions.SortContinuationResourceSurrogateId);

                if (sortCte != null)
                {
                    sqlBuilder.AppendLine($",{sortCte}");
                    lastCteName = sortCteName;
                    parserOptions.LastCteName = lastCteName;
                    hasSortCte = true;
                }
            }

            // *********************************************************************** Apply Count ***********************************************************************
            if (!parserOptions.GetTotalCount)
            {
                var cteName = $"cte{cteIndex}";
                parserOptions.CteNumber = cteIndex;
                cteIndex++;

                // When a sort CTE exists, use SortValue for ordering; otherwise use ResourceTypeId/ResourceSurrogateId
                string sortDir = parserOptions.SortDescending ? "DESC" : "ASC";
                string rowOrderBy;
                string outerOrderBy;
                if (hasSortCte)
                {
                    rowOrderBy = $"CASE WHEN r.SortValue IS NULL THEN 1 ELSE 0 END ASC, r.SortValue {sortDir}, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC";
                    outerOrderBy = $"CASE WHEN r.SortValue IS NULL THEN 1 ELSE 0 END ASC, r.SortValue {sortDir}, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC";
                }
                else
                {
                    rowOrderBy = $"r.ResourceTypeId {sortDir}, r.ResourceSurrogateId {sortDir}";
                    outerOrderBy = $"r.ResourceTypeId {sortDir}, r.ResourceSurrogateId {sortDir}";
                }

                sqlBuilder.BeginCte(cteName)
                    .SelectWithModifier($"TOP {parserOptions.Count + 1}", "*", "IsMatch = 1", "IsPartial = 0", $"Row = ROW_NUMBER() OVER (ORDER BY {rowOrderBy})")
                    .From(lastCteName, "r")
                    .OrderBy(outerOrderBy)
                    .EndCte();

                lastCteName = cteName;
                parserOptions.LastCteName = lastCteName;
            }

            // *********************************************************************** Include Parameters ***********************************************************************
            if (includeParameters.Count > 0 && !parserOptions.GetTotalCount)
            {
                var baseCteName = lastCteName;

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
                            // Create union CTE for iterate dependencies (no Row column since these are include CTEs)
                            ParserUtil.AddUnionCte(sqlBuilder, unionCteName, dependencyCteNames, includeRow: false);
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
                    parserOptions.CteNumber = cteIndex;
                    parserOptions.LastCteName = includeLastCteName;
                    parserOptions.IsIterateInclude = orderedInclude.IsIterate;
                    cteIndex++;

                    // Choose the appropriate parser based on whether this is _include or _revinclude
                    ISqlParser parser = orderedInclude.ParameterName.StartsWith("_revinclude", StringComparison.OrdinalIgnoreCase)
                        ? _revIncludeSqlParser
                        : _includeSqlParser;

                    parser.Parse(orderedInclude.ParameterName, orderedInclude.Value, parserOptions);

                    includeCteNames.Add(includeCteName);
                    orderedInclude.CteNames.Add(includeCteName);
                }

                sqlBuilder.AppendLine();

                var unionCte = $"cte{cteIndex}";
                cteIndex++;

                // If there is an includes continuation token, we don't include the matched resources in the final result set. So we don't need to union the include CTEs with the base CTE
                bool includeRow = true;
                if (parserOptions.IncludesContinuationToken != null)
                {
                    includeCteNames.RemoveAt(0);
                    includeRow = false;
                }

                ParserUtil.AddUnionCte(sqlBuilder, unionCte, includeCteNames, includeSort: hasSortCte, includeRow: includeRow);

                lastCteName = unionCte;
                cteIndex++;
            }

            sqlBuilder.AppendLine();

            // *********************************************************************** Get Resources ***********************************************************************
            // If this is a count query, return count instead of full results
            if (parserOptions.GetTotalCount)
            {
                sqlBuilder.Select($"COUNT_BIG(*) AS Total")
                    .From(lastCteName);
            }
            else
            {
                // Build the ORDER BY clause based on sort parameters
                string orderByClause;
                bool hasSortValue = false;

                if (!string.IsNullOrEmpty(parserOptions.SortParameterName) && parserOptions.IncludesContinuationToken == null)
                {
                    if (parserOptions.SortIsSpecialParameter)
                    {
                        // Special parameters map directly to Resource table columns
                        if (parserOptions.SortParameterName.Equals(SearchParameterNames.LastUpdated, StringComparison.OrdinalIgnoreCase))
                        {
                            // _lastUpdated maps to ResourceSurrogateId (which encodes timestamp)
                            orderByClause = parserOptions.SortDescending
                                ? "t.IsMatch DESC, t.ResourceSurrogateId DESC"
                                : "t.IsMatch DESC, t.ResourceSurrogateId ASC";
                        }
                        else if (parserOptions.SortParameterName.Equals(SearchParameterNames.ResourceType, StringComparison.OrdinalIgnoreCase))
                        {
                            // _type maps to ResourceTypeId
                            orderByClause = parserOptions.SortDescending
                                ? "t.IsMatch DESC, t.ResourceTypeId DESC, t.ResourceSurrogateId DESC"
                                : "t.IsMatch DESC, t.ResourceTypeId ASC, t.ResourceSurrogateId ASC";
                        }
                        else
                        {
                            // Fallback to default ordering
                            orderByClause = "t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                        }
                    }
                    else
                    {
                        // Regular search parameters - use SortSqlParser
                        hasSortValue = hasSortCte;

                        orderByClause = SortSqlParser.CreateOrderByClause(parserOptions.SortDescending, hasSortValue);
                    }
                }
                else
                {
                    // No sort parameter - use default ordering
                    orderByClause = "t.IsMatch DESC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 1 THEN t.ResourceSurrogateId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceTypeId ELSE NULL END) ASC, (CASE WHEN t.IsMatch = 0 THEN t.ResourceSurrogateId ELSE NULL END) ASC";
                }

                // Build the SELECT statement - include SortValue if it exists
                var selectColumns = "r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource";
                if (hasSortValue)
                {
                    selectColumns = $"r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(f.IsMatch AS bit) AS IsMatch, CAST(f.IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource, f.SortValue";
                }

                sqlBuilder.Select($"*")
                    .From("(")
                    .IncreaseIndent()
                    .SelectWithModifier("DISTINCT", selectColumns)
                    .From("dbo.Resource", "r")
                    .JoinMultiLine("INNER", lastCteName, "f", "r.ResourceSurrogateId = f.ResourceSurrogateId", "r.ResourceTypeId = f.ResourceTypeId")
                    .Where("r.IsHistory = 0")
                    .And("r.IsDeleted = 0")
                    .AppendLine(") AS t")
                    .OrderBy(orderByClause);
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
        /// <exception cref="ArgumentNullException">If the name is null or whitespace.</exception>
        /// <exception cref="ArgumentException">If the search parameter is not supported or the parser is not found.</exception>
        private void Parse(string name, string value, ParserOptions options)
        {
            var parser = GetParser(name, options.ResourceTypes.FirstOrDefault());
            if (parser == null)
            {
                throw new ArgumentException($"Parser not found for search parameter '{name}'.");
            }

            parser.Parse(name, value, options);
        }

        /// <summary>
        /// Orders include parameters so that _include:iterate parameters are processed after
        /// all _include parameters that produce the resources they depend on.
        /// Also handles _revinclude:iterate which has reversed dependency logic.
        ///
        /// Examples:
        /// - _include:iterate requires the SOURCE type to exist:
        ///   Patient?_include=Observation:subject&amp;_include:iterate=Patient:organization
        ///   → Observation must be produced first, then we can follow Patient.organization
        ///
        /// - _revinclude:iterate requires the TARGET type to exist:
        ///   Patient?_include=Patient:organization&amp;_revinclude:iterate=Observation:subject:Organization
        ///   → Organization must be produced first, then we can find Observations that reference it
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
                        IsRevInclude = kvp.Key.StartsWith("_revinclude", StringComparison.OrdinalIgnoreCase),
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
                var requiredResourceTypes = new HashSet<short>();

                // For _include:iterate, we need the SOURCE types to be present (to follow their references)
                // For _revinclude:iterate, we need the TARGET types to be present (to find what references them)
                if (iterateInclude.IsRevInclude)
                {
                    // For revinclude:iterate, we need the target resource types from the parameter
                    var targetTypes = GetIncludeTargetResourceTypes(iterateInclude.Value);
                    foreach (var targetType in targetTypes)
                    {
                        requiredResourceTypes.Add(targetType);
                    }
                }
                else
                {
                    // For include:iterate, we need the source resource type
                    var parts = iterateInclude.Value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        try
                        {
                            var sourceResourceTypeId = _sqlServerFhirModel.GetResourceTypeId(parts[0]);
                            requiredResourceTypes.Add(sourceResourceTypeId);
                        }
                        catch
                        {
                            // Invalid resource type, skip
                        }
                    }
                }

                // Check all other includes to see if they produce any of the required resource types
                for (int j = 0; j < allIncludes.Count; j++)
                {
                    if (i == j)
                    {
                        continue; // Don't depend on self
                    }

                    var potentialDependency = allIncludes[j];
                    var producedTypes = new HashSet<short>();

                    // Get all resource types this include could produce
                    // _include produces target types, _revinclude produces source types
                    var typesProduced = GetProducedResourceTypes(potentialDependency.Value, potentialDependency.IsRevInclude);
                    foreach (var typeId in typesProduced)
                    {
                        producedTypes.Add(typeId);
                    }

                    // If this include produces any of the required resource types, add it as a dependency
                    if (requiredResourceTypes.Overlaps(producedTypes))
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
        /// Gets the resource types produced by an include or revinclude operation.
        /// For _include, this returns the target types (what's referenced).
        /// For _revinclude, this returns the source types (what's doing the referencing).
        /// </summary>
        /// <param name="includeValue">The include parameter value (e.g., "Patient:organization" or "Observation:subject:Patient").</param>
        /// <param name="isRevInclude">True if this is a _revinclude operation, false for _include.</param>
        /// <returns>A list of resource type IDs that this operation produces.</returns>
        private List<short> GetProducedResourceTypes(string includeValue, bool isRevInclude)
        {
            var parts = includeValue.Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return new List<short>();
            }

            if (isRevInclude)
            {
                // For _revinclude, we produce the SOURCE resource types (what's doing the referencing)
                // This is the first part of the value
                var producedTypes = new List<short>();
                try
                {
                    var sourceTypeId = _sqlServerFhirModel.GetResourceTypeId(parts[0]);
                    producedTypes.Add(sourceTypeId);
                }
                catch
                {
                    // Invalid resource type
                }

                return producedTypes;
            }
            else
            {
                // For _include, we produce the TARGET resource types (what's referenced)
                // Use the existing method for this
                return GetIncludeTargetResourceTypes(includeValue);
            }
        }

        private static Dictionary<string, IList<string>> DeepCopyParameters(IDictionary<string, IList<string>> original)
        {
            var copy = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in original)
            {
                copy[kvp.Key] = new List<string>(kvp.Value);
            }

            return copy;
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

            public bool IsRevInclude { get; set; }

            public HashSet<int> DependsOnIndices { get; set; } = new HashSet<int>();

            public List<string> CteNames { get; set; } = new List<string>();
        }
    }
}

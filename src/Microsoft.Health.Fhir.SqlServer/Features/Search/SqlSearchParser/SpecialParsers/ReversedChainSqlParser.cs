// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Parser for reversed chained search using the _has parameter.
    /// Example: Patient?_has:Observation:subject:code=1234-5
    /// Finds resources that are referenced BY other resources with particular values.
    /// </summary>
    public class ReversedChainSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly SearchParameterSqlParser _parserSource;
        private readonly ISqlServerFhirModel _model;

        public ReversedChainSqlParser(SqlSearchParameterDefinitionManager parameterCollection, SearchParameterSqlParser parserSource, ISqlServerFhirModel model)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(parserSource);
            ArgumentNullException.ThrowIfNull(model);

            _parameterCollection = parameterCollection;
            _parserSource = parserSource;
            _model = model;
        }

        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            // Parse the _has parameter format: _has:<resourceType>:<referenceParam>:<searchParam>
            if (!name.StartsWith("_has:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid _has parameter format. Expected format: _has:<resourceType>:<referenceParam>:<searchParam>", nameof(name));
            }

            var parts = name.Split(':', 4);
            if (parts.Length < 4)
            {
                throw new ArgumentException("Invalid _has parameter format. Expected format: _has:<resourceType>:<referenceParam>:<searchParam>", nameof(name));
            }

            var sourceResourceType = parts[1]; // The resource type that references the target
            var referenceParamCode = parts[2]; // The reference parameter on the source resource
            var searchParamCode = parts[3]; // The search parameter on the source resource to filter by

            // Get the resource type ID for the source resource
            short sourceResourceTypeId;
            try
            {
                sourceResourceTypeId = _model.GetResourceTypeId(sourceResourceType);
            }
            catch
            {
                throw new ArgumentException($"Unknown resource type '{sourceResourceType}' in _has parameter.", nameof(name));
            }

            // Look up the reference parameter on the source resource type
            var referenceParameter = _parameterCollection.GetByCode(referenceParamCode, sourceResourceTypeId);
            if (referenceParameter == null)
            {
                throw new ArgumentException($"Reference parameter '{referenceParamCode}' is not supported for resource type '{sourceResourceType}'.", nameof(name));
            }

            var sqlBuilder = new StringBuilder();
            var refChainCteName = $"{options.CteName}chain{options.ChainLevel}_ref";
            var searchCteName = $"{options.CteName}chain{options.ChainLevel}_search";

            // Add comma separator if there's a previous CTE
            if (!string.IsNullOrEmpty(options.LastCteName))
            {
                sqlBuilder.Append(',');
            }

            // Check if the search parameter is _type (special case)
            if (!searchParamCode.Equals(KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                // Parse the search parameter to filter source resources
                var searchParser = _parserSource.GetParser(searchParamCode, sourceResourceTypeId);
                var searchParserOptions = new ParserOptions
                {
                    CteName = options.CteName,
                    ResourceTypes = new List<short> { sourceResourceTypeId },
                    ChainLevel = options.ChainLevel + 1,
                };

                // Generate the search parameter filter CTE
                var searchSql = searchParser.Parse(searchParamCode, value, searchParserOptions);
                if (string.IsNullOrEmpty(searchSql))
                {
                    throw new ArgumentException("Failed to parse search parameter for _has query.", nameof(name));
                }

                // Wrap the search SQL in a CTE if it's not already nested
                if (searchParser is ReversedChainSqlParser || searchParser is ChainedSqlParser)
                {
                    // Include the search parameter CTE directly for nested chains
                    sqlBuilder.AppendLine(searchSql);
                }
                else
                {
                    sqlBuilder.AppendLine($"{searchCteName} AS (");
                    sqlBuilder.AppendLine(searchSql);
                    sqlBuilder.AppendLine(")");
                }

                // Create the CTE for the reverse reference join
                sqlBuilder.AppendLine($",{refChainCteName} AS (");
                sqlBuilder.AppendLine("  SELECT DISTINCT");
                sqlBuilder.AppendLine("    target.ResourceTypeId AS ResourceTypeId,");
                sqlBuilder.AppendLine("    target.ResourceSurrogateId AS ResourceSurrogateId,");
                sqlBuilder.AppendLine("    1 AS IsMatch,");
                sqlBuilder.AppendLine("    0 AS IsPartial");
                sqlBuilder.AppendLine("  FROM dbo.ReferenceSearchParam refSource");

                // Join with the filtered source resources
                sqlBuilder.AppendLine($"  JOIN {searchCteName} source ON source.ResourceTypeId = refSource.ResourceTypeId AND source.ResourceSurrogateId = refSource.ResourceSurrogateId");

                // Join with the target resources (the ones being referenced)
                sqlBuilder.AppendLine("  JOIN dbo.Resource target ON refSource.ReferenceResourceTypeId = target.ResourceTypeId AND refSource.ReferenceResourceId = target.ResourceId");

                // Join with the previous CTE to continue the chain (if applicable)
                if (options.LastCteName != null)
                {
                    var prevRef = options.ChainLevel > 0 ? "prev.Ref" : "prev.";
                    sqlBuilder.AppendLine($"  JOIN {options.LastCteName} prev ON {prevRef}ResourceTypeId = target.ResourceTypeId AND {prevRef}ResourceSurrogateId = target.ResourceSurrogateId");
                }

                // Filter by the reference parameter
                sqlBuilder.AppendLine($"  WHERE refSource.SearchParamId = {referenceParameter.Id}");
                sqlBuilder.AppendLine("  AND target.IsHistory = 0");
                sqlBuilder.AppendLine("  AND target.IsDeleted = 0");

                // Add base filters only on the first CTE
                if (options.LastCteName == null)
                {
                    if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                    {
                        var targetResourceTypeIds = string.Join(", ", options.ResourceTypes);
                        sqlBuilder.AppendLine($"  AND target.ResourceTypeId IN ({targetResourceTypeIds})");
                    }

                    if (options.ContinuationToken != null)
                    {
                        sqlBuilder.AppendLine($"  AND target.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                        if (options.ContinuationToken.ResourceTypeId != null)
                        {
                            sqlBuilder.AppendLine($"  AND target.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                        }
                    }
                }

                sqlBuilder.AppendLine(")");
            }
            else
            {
                // Special handling for _type parameter
                // Parse the value as comma-separated resource type names
                var sourceTypeIds = value.Split(',').Select(v => _model.GetResourceTypeId(v.Trim())).ToList();

                // Create the CTE for the reverse reference join with type filter
                sqlBuilder.AppendLine($"{refChainCteName} AS (");
                sqlBuilder.AppendLine("  SELECT DISTINCT");
                sqlBuilder.AppendLine("    target.ResourceTypeId AS ResourceTypeId,");
                sqlBuilder.AppendLine("    target.ResourceSurrogateId AS ResourceSurrogateId,");
                sqlBuilder.AppendLine("    1 AS IsMatch,");
                sqlBuilder.AppendLine("    0 AS IsPartial");
                sqlBuilder.AppendLine("  FROM dbo.ReferenceSearchParam refSource");

                // Join with the source resources (filtered by type)
                sqlBuilder.AppendLine("  JOIN dbo.Resource source ON source.ResourceTypeId = refSource.ResourceTypeId AND source.ResourceSurrogateId = refSource.ResourceSurrogateId");

                // Join with the target resources (the ones being referenced)
                sqlBuilder.AppendLine("  JOIN dbo.Resource target ON refSource.ReferenceResourceTypeId = target.ResourceTypeId AND refSource.ReferenceResourceId = target.ResourceId");

                // Join with the previous CTE to continue the chain (if applicable)
                if (options.LastCteName != null)
                {
                    sqlBuilder.AppendLine($"  JOIN {options.LastCteName} prev ON prev.ResourceTypeId = target.ResourceTypeId AND prev.ResourceSurrogateId = target.ResourceSurrogateId");
                }

                // Filter by the reference parameter and source type
                sqlBuilder.AppendLine($"  WHERE refSource.SearchParamId = {referenceParameter.Id}");
                sqlBuilder.AppendLine($"  AND source.ResourceTypeId IN ({string.Join(",", sourceTypeIds)})");
                sqlBuilder.AppendLine("  AND source.IsHistory = 0");
                sqlBuilder.AppendLine("  AND source.IsDeleted = 0");
                sqlBuilder.AppendLine("  AND target.IsHistory = 0");
                sqlBuilder.AppendLine("  AND target.IsDeleted = 0");

                // Add base filters only on the first CTE
                if (options.LastCteName == null)
                {
                    if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                    {
                        var targetResourceTypeIds = string.Join(", ", options.ResourceTypes);
                        sqlBuilder.AppendLine($"  AND target.ResourceTypeId IN ({targetResourceTypeIds})");
                    }

                    if (options.ContinuationToken != null)
                    {
                        sqlBuilder.AppendLine($"  AND target.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                        if (options.ContinuationToken.ResourceTypeId != null)
                        {
                            sqlBuilder.AppendLine($"  AND target.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                        }
                    }
                }

                sqlBuilder.AppendLine(")");
            }

            // Create the final CTE with row numbering
            if (options.ChainLevel == 0)
            {
                sqlBuilder.AppendLine($",{options.CteName} AS (")
                    .AppendLine("  SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row")
                    .AppendLine($"  FROM {refChainCteName}")
                    .AppendLine(")");
            }
            else
            {
                var parentCteName = $"{options.CteName}chain{options.ChainLevel - 1}";

                if (options.ParentIsForwardChain)
                {
                    sqlBuilder.AppendLine($",{parentCteName}_search AS (")
                        .AppendLine("  SELECT DISTINCT parent.ResourceTypeId, parent.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY parent.ResourceTypeId ASC, parent.ResourceSurrogateId ASC) AS Row")
                        .AppendLine($"  FROM {refChainCteName} child")
                        .AppendLine($"    JOIN {parentCteName}_ref parent ON parent.RefResourceTypeId = child.ResourceTypeId AND parent.RefResourceSurrogateId = child.ResourceSurrogateId")
                        .AppendLine(")");
                }
                else
                {
                    sqlBuilder.AppendLine($",{parentCteName}_search AS (")
                        .AppendLine("  SELECT DISTINCT ResourceTypeId, ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row")
                        .AppendLine($"  FROM {refChainCteName}")
                        .AppendLine(")");
                }
            }

            return sqlBuilder.ToString();
        }
    }
}

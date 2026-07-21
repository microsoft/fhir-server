// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

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

        public void Parse(string name, string value, ParserOptions options)
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

            var sourceResourceType = parts[1]; // The resource type that references the target (e.g., Observation)
            var referenceParamCode = parts[2]; // The reference parameter on the source resource (e.g., patient)
            var searchParamCode = parts[3]; // The search parameter on the source resource to filter by (e.g., code)

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

            var cteName = $"cte{options.CteNumber}";
            var builder = options.SqlQueryBuilder;
            var refChainCteName = $"{cteName}chain{options.ChainLevel}_ref";

            // Step 1: Create the _ref CTE that finds reference links from source to target.
            // For reverse chain, we flip the column naming:
            // - RefResourceTypeId/RefResourceSurrogateId = SOURCE (Observation) - what we'll search
            // - ResourceTypeId/ResourceSurrogateId = TARGET (Patient) - what we'll output
            builder.BeginCte(refChainCteName);
            builder.Select(
                "refSource.ResourceTypeId AS RefResourceTypeId",
                "refSource.ResourceSurrogateId AS RefResourceSurrogateId",
                "refTarget.ResourceTypeId AS ResourceTypeId",
                "refTarget.ResourceSurrogateId AS ResourceSurrogateId");

            builder.From("dbo.ReferenceSearchParam", "refSource");
            builder.InnerJoin("dbo.Resource", "refTarget", "refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");

            // If we have a previous CTE, join to it (for nested _has or combined with other params)
            if (options.LastCteName != null)
            {
                // When nested in a chain (ChainLevel > 0), the previous CTE is a _ref CTE
                // whose RefResource columns represent the SOURCE resources we want to constrain against
                var prevSurrogateCol = options.ChainLevel > 0 ? "RefResourceSurrogateId" : "ResourceSurrogateId";
                var prevTypeCol = options.ChainLevel > 0 ? "RefResourceTypeId" : "ResourceTypeId";
                builder.InnerJoin(
                    options.LastCteName,
                    "prev",
                    $"prev.{prevSurrogateCol} = refTarget.ResourceSurrogateId AND prev.{prevTypeCol} = refTarget.ResourceTypeId");
            }

            builder.Where($"refSource.SearchParamId = {referenceParameter.Id}");
            builder.And($"refSource.ResourceTypeId = {sourceResourceTypeId}");
            ParserUtil.AddHistoryAndDeletedCheck(builder, "refTarget");

            // Add base filters on the first level
            if (options.LastCteName == null)
            {
                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var targetResourceTypeIds = string.Join(", ", options.ResourceTypes);
                    builder.And($"refTarget.ResourceTypeId IN ({targetResourceTypeIds})");
                }

                if (options.ContinuationToken != null)
                {
                    var surrogateOperator = options.SortDescending ? "<" : ">";
                    builder.And($"refTarget.ResourceSurrogateId {surrogateOperator} {options.ContinuationToken.ResourceSurrogateId}");

                    if (options.ContinuationToken.ResourceTypeId != null)
                    {
                        var typeOperator = options.SortDescending ? "<" : ">";
                        builder.And($"refTarget.ResourceTypeId {typeOperator}= {options.ContinuationToken.ResourceTypeId}");
                    }
                }
            }

            builder.EndCte();

            // Step 2: Create the search filter on source resources
            string searchChainCteName;

            if (!searchParamCode.Equals(KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                // Parse the search parameter to filter source resources
                var searchParser = _parserSource.GetParser(searchParamCode, sourceResourceTypeId);
                var innerOptions = new ParserOptions
                {
                    CteNumber = options.CteNumber,
                    ResourceTypes = new List<short> { sourceResourceTypeId },
                    ChainLevel = options.ChainLevel + 1,
                    LastCteName = refChainCteName,
                    SqlQueryBuilder = builder,
                };
                searchParser.Parse(searchParamCode, value, innerOptions);

                // Use the result CTE name from the inner parser (handles nested chains)
                searchChainCteName = innerOptions.ResultCteName ?? $"{cteName}chain{options.ChainLevel + 1}";
            }
            else
            {
                // Special handling for _type parameter - filter by source resource type
                searchChainCteName = $"{cteName}chain{options.ChainLevel + 1}";
                var sourceTypeIds = value.Split(',').Select(v => _model.GetResourceTypeId(v.Trim())).ToList();
                builder.BeginCte(searchChainCteName);
                builder.SelectWithModifier("DISTINCT", "r.RefResourceSurrogateId AS ResourceSurrogateId", "r.RefResourceTypeId AS ResourceTypeId");
                builder.From(refChainCteName, "r");
                builder.Where($"r.RefResourceTypeId IN ({string.Join(",", sourceTypeIds)})");
                builder.EndCte();
            }

            // Step 3: Create the final CTE that maps matching sources back to targets
            string resultCteName;
            if (options.ChainLevel == 0)
            {
                // Final level - output the target resources (Patients)
                resultCteName = cteName;
                builder.BeginCte(resultCteName);
                builder.SelectWithModifier(
                    "DISTINCT",
                    "ref_cte.ResourceTypeId",
                    "ref_cte.ResourceSurrogateId");
                builder.From(searchChainCteName, "search");
                builder.InnerJoin(
                    refChainCteName,
                    "ref_cte",
                    "ref_cte.RefResourceSurrogateId = search.ResourceSurrogateId AND ref_cte.RefResourceTypeId = search.ResourceTypeId");
                builder.EndCte();
            }
            else
            {
                // Nested level - output target resources for the parent chain to use
                var parentCteName = $"{cteName}chain{options.ChainLevel - 1}";
                resultCteName = $"{parentCteName}_search";
                builder.BeginCte(resultCteName);
                builder.SelectWithModifier(
                    "DISTINCT",
                    "ref_cte.ResourceTypeId",
                    "ref_cte.ResourceSurrogateId");
                builder.From(searchChainCteName, "search");
                builder.InnerJoin(
                    refChainCteName,
                    "ref_cte",
                    "ref_cte.RefResourceSurrogateId = search.ResourceSurrogateId AND ref_cte.RefResourceTypeId = search.ResourceTypeId");
                builder.EndCte();
            }

            options.ResultCteName = resultCteName;
        }
    }
}

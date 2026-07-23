// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class ChainedSqlParser : ISqlParser
    {
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly SearchParameterSqlParser _parserSource;
        private readonly ISqlServerFhirModel _model;

        public ChainedSqlParser(SqlSearchParameterDefinitionManager parameterCollection, SearchParameterSqlParser parserSource, ISqlServerFhirModel model)
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

            // Split the chained parameter name
            var parts = name.Split('.', 2);
            if (parts.Length < 2)
            {
                throw new ArgumentException("Invalid chained parameter format.", nameof(name));
            }

            var firstCode = parts[0];
            var remainingChain = parts[1];

            // Look up the first parameter (should be a reference parameter)
            var parameter = _parameterCollection.GetByCode(firstCode, options.ResourceTypes.FirstOrDefault());
            if (parameter == null)
            {
                throw new ArgumentException($"Search parameter '{firstCode}' is not supported for resource type '{options.ResourceTypes.FirstOrDefault()}'.", nameof(name));
            }

            var resourceTypeIds = parameter.SearchParameterInfo.TargetResourceTypes.Select(t => _model.GetResourceTypeId(t)).ToList();

            var builder = options.SqlQueryBuilder;

            var chainCteName = $"cte{options.CteNumber}chain{options.ChainLevel}_ref";

            builder.BeginCte(chainCteName);
            builder.Select(
                "refSource.ReferenceResourceTypeId AS RefResourceTypeId",
                "refTarget.ResourceSurrogateId AS RefResourceSurrogateId",
                "refSource.ResourceTypeId AS ResourceTypeId",
                "refSource.ResourceSurrogateId AS ResourceSurrogateId");

            builder.From("dbo.ReferenceSearchParam", "refSource");
            builder.InnerJoin("dbo.Resource", "refTarget", "refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId");
            builder.InnerJoin(
                options.LastCteName ?? "dbo.Resource",
                "source",
                $"source.{(options.ChainLevel > 0 ? "RefResource" : "Resource")}SurrogateId = refSource.ResourceSurrogateId AND source.{(options.ChainLevel > 0 ? "RefResource" : "Resource")}TypeId = refSource.ResourceTypeId");

            builder.Where($"refSource.SearchParamId = {parameter.Id}");
            ParserUtil.AddHistoryAndDeletedCheck(builder, "refTarget");

            if (firstCode.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                var resourceType = firstCode.Split(':')[1];
                resourceTypeIds = new List<short> { _model.GetResourceTypeId(resourceType) };
                builder.And($"refSource.ReferenceResourceTypeId = {resourceTypeIds[0]}");
            }

            // Add base filters only on the first CTE
            ParserUtil.AddFirstCteFilters(builder, options, "source");

            if (!remainingChain.Equals(KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
            {
                builder.EndCte();

                // Recursively parse the remaining chain
                // Get the parameter for the remaining chain to find the right parser
                var remainingParameterParser = _parserSource.GetParser(remainingChain, resourceTypeIds[0]);

                var searchChainLevel = options.ChainLevel + 1;
                var innerOptions = new ParserOptions
                {
                    CteNumber = options.CteNumber,
                    LastCteName = chainCteName,
                    ChainLevel = searchChainLevel,
                    ResourceTypes = resourceTypeIds,
                    ParentIsForwardChain = true,
                    SqlQueryBuilder = builder,
                };
                remainingParameterParser.Parse(remainingChain, value, innerOptions);

                // Use the result CTE name from the inner parser (handles nested chains)
                chainCteName = innerOptions.ResultCteName ?? $"cte{options.CteNumber}chain{searchChainLevel}";
            }
            else
            {
                var valueTypeIds = value.Split(',').Select(v => _model.GetResourceTypeId(v)).ToList();
                builder.And($"refTarget.ResourceTypeId IN ({string.Join(",", valueTypeIds)})");
                builder.EndCte();
            }

            var baseCteName = $"cte{options.CteNumber}";
            var refCteName = $"cte{options.CteNumber}chain{options.ChainLevel}_ref";
            string resultCteName;

            // When _type was the terminal (chainCteName == refCteName), the ref CTE already
            // contains the filtered results — just select source resources directly from it.
            bool typeWasTerminal = chainCteName == refCteName;

            if (options.ChainLevel == 0)
            {
                resultCteName = baseCteName;
                builder.BeginCte(resultCteName);
                if (typeWasTerminal)
                {
                    // Source resources are directly in the ref CTE
                    builder.SelectWithModifier(
                        "DISTINCT",
                        "ResourceTypeId",
                        "ResourceSurrogateId");
                    builder.From(refCteName);
                }
                else
                {
                    // Join the search result (matching targets) back to the ref CTE to get source resources
                    builder.SelectWithModifier(
                        "DISTINCT",
                        "ref_cte.ResourceTypeId",
                        "ref_cte.ResourceSurrogateId");
                    builder.From(chainCteName, "search");
                    builder.InnerJoin(
                        refCteName,
                        "ref_cte",
                        "ref_cte.RefResourceSurrogateId = search.ResourceSurrogateId AND ref_cte.RefResourceTypeId = search.ResourceTypeId");
                }

                builder.EndCte();
            }
            else
            {
                var parentCteName = $"{baseCteName}chain{options.ChainLevel - 1}";
                resultCteName = $"{parentCteName}_search";

                if (typeWasTerminal)
                {
                    // Source resources are directly in the ref CTE
                    builder.BeginCte(resultCteName);
                    builder.SelectWithModifier(
                        "DISTINCT",
                        "ResourceTypeId",
                        "ResourceSurrogateId");
                    builder.From(refCteName);
                    builder.EndCte();
                }
                else if (options.ParentIsForwardChain)
                {
                    builder.BeginCte(resultCteName);
                    builder.SelectWithModifier(
                        "DISTINCT",
                        "parent.ResourceTypeId",
                        "parent.ResourceSurrogateId");
                    builder.From(chainCteName, "child");
                    builder.InnerJoin(
                        refCteName,
                        "parent",
                        "parent.RefResourceTypeId = child.ResourceTypeId AND parent.RefResourceSurrogateId = child.ResourceSurrogateId");
                    builder.EndCte();
                }
                else
                {
                    builder.BeginCte(resultCteName);
                    builder.SelectWithModifier(
                        "DISTINCT",
                        "ResourceTypeId",
                        "ResourceSurrogateId");
                    builder.From(chainCteName);
                    builder.EndCte();
                }
            }

            options.ResultCteName = resultCteName;
        }
    }
}

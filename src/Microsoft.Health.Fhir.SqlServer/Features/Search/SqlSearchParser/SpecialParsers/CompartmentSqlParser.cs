// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Handles compartment searches by querying the ReferenceSearchParam table.
    /// A compartment search (e.g., Patient/123/Observation) finds resources that have a reference
    /// search parameter pointing to the compartment owner. The compartment definition defines which
    /// resource types and search parameters are relevant for each compartment type.
    /// </summary>
    public class CompartmentSqlParser : ISqlParser
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;

        public CompartmentSqlParser(
            ISqlServerFhirModel model,
            SqlSearchParameterDefinitionManager parameterCollection,
            ICompartmentDefinitionManager compartmentDefinitionManager)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(parameterCollection);
            ArgumentNullException.ThrowIfNull(compartmentDefinitionManager);
            _model = model;
            _parameterCollection = parameterCollection;
            _compartmentDefinitionManager = compartmentDefinitionManager;
        }

        /// <summary>
        /// Generates a CTE that filters resources by compartment membership using reference search params.
        /// </summary>
        /// <param name="name">The compartment type (e.g., "Patient", "Device").</param>
        /// <param name="value">The compartment owner's resource ID (e.g., "123").</param>
        /// <param name="options">Parser options containing CTE info and resource type filters.</param>
        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!Enum.TryParse<CompartmentType>(name, out var compartmentType))
            {
                throw new InvalidOperationException($"Invalid compartment type: {name}");
            }

            // Get resource types that belong to this compartment
            if (!_compartmentDefinitionManager.TryGetResourceTypes(compartmentType, out HashSet<string> allResourceTypes))
            {
                throw new InvalidOperationException($"No resource types found for compartment type: {name}");
            }

            // Filter to only the requested resource types if specified
            var resourceTypesToSearch = allResourceTypes;
            if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
            {
                var requestedTypeNames = options.ResourceTypes
                    .Select(id => _model.GetResourceTypeName(id))
                    .Where(n => n != null)
                    .ToHashSet();
                resourceTypesToSearch = allResourceTypes.Where(rt => requestedTypeNames.Contains(rt)).ToHashSet();
            }

            // Build a mapping of search param ID -> applicable resource type IDs
            var searchParamToResourceTypes = new Dictionary<short, HashSet<short>>();

            foreach (var resourceType2 in resourceTypesToSearch)
            {
                if (_compartmentDefinitionManager.TryGetSearchParams(resourceType2, compartmentType, out HashSet<string> searchParamNames))
                {
                    foreach (var searchParamName in searchParamNames)
                    {
                        try
                        {
                            var paramWrapper = _parameterCollection.GetByCode(searchParamName, resourceType2);
                            if (paramWrapper != null)
                            {
                                short paramId = (short)paramWrapper.Id;
                                if (!searchParamToResourceTypes.TryGetValue(paramId, out var rtSet))
                                {
                                    rtSet = new HashSet<short>();
                                    searchParamToResourceTypes[paramId] = rtSet;
                                }

                                short rtId = _model.GetResourceTypeId(resourceType2);
                                rtSet.Add(rtId);
                            }
                        }
                        catch
                        {
                            // Skip search params that can't be resolved
                        }
                    }
                }
            }

            if (searchParamToResourceTypes.Count == 0)
            {
                // No valid search params found - generate a CTE that returns nothing
                var sqlBuilder = options.SqlQueryBuilder;
                sqlBuilder.BeginCte($"cte{options.CteNumber}");
                sqlBuilder.Select("r.ResourceTypeId", "r.ResourceSurrogateId");
                sqlBuilder.From("dbo.Resource", "r");
                sqlBuilder.Where("1 = 0");
                sqlBuilder.EndCte();
                return;
            }

            // Generate the CTE using ReferenceSearchParam table
            var sql = options.SqlQueryBuilder;
            var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
            short compartmentResourceTypeId = _model.GetResourceTypeId(name);

            sql.BeginCte($"cte{options.CteNumber}");
            sql.Select("r.ResourceTypeId", "r.ResourceSurrogateId");
            sql.From("dbo.Resource", "r");
            sql.Join("INNER", "dbo.ReferenceSearchParam", "ref1", "r.ResourceTypeId = ref1.ResourceTypeId AND r.ResourceSurrogateId = ref1.ResourceSurrogateId");

            sql.Where("r.IsHistory = 0")
                .And("r.IsDeleted = 0")
                .And($"ref1.ReferenceResourceTypeId = {compartmentResourceTypeId}")
                .And($"ref1.ReferenceResourceId = '{escapedValue}'");

            // Build the OR condition: (SearchParamId = X AND ResourceTypeId IN (...)) OR ...
            var orConditions = new List<string>();
            foreach (var kvp in searchParamToResourceTypes)
            {
                short searchParamId = kvp.Key;
                var resourceTypeIds = kvp.Value;
                var rtIdsStr = string.Join(", ", resourceTypeIds.OrderBy(x => x));

                if (resourceTypeIds.Count == 1)
                {
                    orConditions.Add($"(ref1.SearchParamId = {searchParamId} AND r.ResourceTypeId = {rtIdsStr})");
                }
                else
                {
                    orConditions.Add($"(ref1.SearchParamId = {searchParamId} AND r.ResourceTypeId IN ({rtIdsStr}))");
                }
            }

            sql.And($"({string.Join("\n          OR ", orConditions)})");

            // Apply continuation token for pagination
            if (options.ContinuationToken != null)
            {
                sql.And($"r.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                if (options.ContinuationToken.ResourceTypeId != null)
                {
                    sql.And($"r.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                }
            }

            sql.EndCte();
        }
    }
}

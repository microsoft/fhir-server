// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Handles SMART compartment searches. A SMART compartment search provides access to:
    /// 1. Resources in the patient's compartment (via reference search params)
    /// 2. The patient's own resource
    /// 3. Universal resources (Location, Organization, Practitioner, Medication, Device)
    /// </summary>
    public class SmartCompartmentSqlParser : ISqlParser
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager;

        private static readonly string[] UniversalResourceTypes = new[]
        {
            KnownResourceTypes.Location,
            KnownResourceTypes.Organization,
            KnownResourceTypes.Practitioner,
            KnownResourceTypes.Medication,
            KnownCompartmentTypes.Device,
        };

        public SmartCompartmentSqlParser(
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
        /// Generates a CTE that filters resources by SMART compartment rules using a UNION of:
        /// 1. Compartment resources (reference search)
        /// 2. The compartment owner's own resource
        /// 3. Universal resources
        /// </summary>
        /// <param name="name">The compartment type (e.g., "Patient").</param>
        /// <param name="value">The compartment owner's resource ID (e.g., "smart-patient-A").</param>
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

            var sql = options.SqlQueryBuilder;
            var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
            short compartmentResourceTypeId = _model.GetResourceTypeId(name);

            // Determine which universal resource types are relevant
            var universalTypeIds = new List<short>();
            foreach (var universalType in UniversalResourceTypes)
            {
                try
                {
                    var typeId = _model.GetResourceTypeId(universalType);
                    // If resource types are filtered, only include universal types that are in the filter
                    if (options.ResourceTypes == null || options.ResourceTypes.Count == 0 || options.ResourceTypes.Contains(typeId))
                    {
                        universalTypeIds.Add(typeId);
                    }
                }
                catch
                {
                    // Skip unknown types
                }
            }

            // Get compartment resource types and their search params
            _compartmentDefinitionManager.TryGetResourceTypes(compartmentType, out HashSet<string> allResourceTypes);
            var searchParamToResourceTypes = new Dictionary<short, HashSet<short>>();

            if (allResourceTypes != null)
            {
                var resourceTypesToSearch = allResourceTypes;
                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var requestedTypeNames = options.ResourceTypes
                        .Select(id => _model.GetResourceTypeName(id))
                        .Where(n => n != null)
                        .ToHashSet();
                    resourceTypesToSearch = allResourceTypes.Where(rt => requestedTypeNames.Contains(rt)).ToHashSet();
                }

                foreach (var resourceType in resourceTypesToSearch)
                {
                    if (_compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentType, out HashSet<string> searchParamNames))
                    {
                        foreach (var searchParamName in searchParamNames)
                        {
                            try
                            {
                                var paramWrapper = _parameterCollection.GetByCode(searchParamName, resourceType);
                                if (paramWrapper != null)
                                {
                                    short paramId = (short)paramWrapper.Id;
                                    if (!searchParamToResourceTypes.TryGetValue(paramId, out var rtSet))
                                    {
                                        rtSet = new HashSet<short>();
                                        searchParamToResourceTypes[paramId] = rtSet;
                                    }

                                    short rtId = _model.GetResourceTypeId(resourceType);
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
            }

            // Generate the SMART compartment CTE using UNION ALL in a subquery
            sql.BeginCte($"cte{options.CteNumber}");
            sql.AppendLine("SELECT ResourceTypeId, ResourceSurrogateId FROM (");
            sql.IncreaseIndent();

            // Part 1: Resources in the compartment (via ReferenceSearchParam)
            if (searchParamToResourceTypes.Count > 0)
            {
                sql.AppendLine("SELECT r.ResourceTypeId, r.ResourceSurrogateId");
                sql.IncreaseIndent();
                sql.AppendLine("FROM dbo.Resource AS r");
                sql.AppendLine("INNER JOIN dbo.ReferenceSearchParam AS ref1 ON r.ResourceTypeId = ref1.ResourceTypeId AND r.ResourceSurrogateId = ref1.ResourceSurrogateId");
                sql.AppendLine($"WHERE r.IsHistory = 0 AND r.IsDeleted = 0");
                sql.AppendLine($"AND ref1.ReferenceResourceTypeId = {compartmentResourceTypeId}");
                sql.AppendLine($"AND ref1.ReferenceResourceId = '{escapedValue}'");

                // Build OR condition for search params
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

                sql.AppendLine($"AND ({string.Join(" OR ", orConditions)})");
                sql.DecreaseIndent();
            }
            else
            {
                // No compartment search params - return empty set for compartment part
                sql.AppendLine("SELECT r.ResourceTypeId, r.ResourceSurrogateId");
                sql.IncreaseIndent();
                sql.AppendLine("FROM dbo.Resource AS r");
                sql.AppendLine("WHERE 1 = 0");
                sql.DecreaseIndent();
            }

            // Part 2: The owner's own resource
            bool ownerInResourceTypes = options.ResourceTypes == null || options.ResourceTypes.Count == 0 || options.ResourceTypes.Contains(compartmentResourceTypeId);
            if (ownerInResourceTypes)
            {
                sql.AppendLine("UNION ALL");
                sql.AppendLine("SELECT r.ResourceTypeId, r.ResourceSurrogateId");
                sql.IncreaseIndent();
                sql.AppendLine("FROM dbo.Resource AS r");
                sql.AppendLine($"WHERE r.ResourceTypeId = {compartmentResourceTypeId}");
                sql.AppendLine($"AND r.ResourceId = '{escapedValue}'");
                sql.AppendLine("AND r.IsHistory = 0 AND r.IsDeleted = 0");
                sql.DecreaseIndent();
            }

            // Part 3: Universal resources
            if (universalTypeIds.Count > 0)
            {
                sql.AppendLine("UNION ALL");
                sql.AppendLine("SELECT r.ResourceTypeId, r.ResourceSurrogateId");
                sql.IncreaseIndent();
                sql.AppendLine("FROM dbo.Resource AS r");
                sql.AppendLine($"WHERE r.ResourceTypeId IN ({string.Join(", ", universalTypeIds.OrderBy(x => x))})");
                sql.AppendLine("AND r.IsHistory = 0 AND r.IsDeleted = 0");
                sql.DecreaseIndent();
            }

            sql.DecreaseIndent();
            sql.AppendLine(") AS smart_union");
            sql.EndCte();
        }
    }
}

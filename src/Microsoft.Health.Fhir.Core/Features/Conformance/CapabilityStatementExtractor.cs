// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{

    /// <summary>
    /// Provides methods to extract resource types, search parameters, and search parameter types from a FHIR capability statement.
    /// </summary>
    /// <example>
    /// Usage example:
    /// <code>
    /// var extractor = new CapabilityStatementExtractor();
    /// var resourceTypes = extractor.GetResourceTypes(capabilityStatement);
    /// var patientSearchParams = extractor.GetSearchParametersForResource(capabilityStatement, "Patient");
    /// foreach (var param in patientSearchParams)
    /// {
    ///     Console.WriteLine($"Parameter: {param.Name}, Type: {param.Type}");
    /// }
    /// </code>
    /// </example>
    public class CapabilityStatementExtractor : ICapabilityStatementExtractor
    {
        /// <summary>
        /// Extracts all resource types from the capability statement.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <returns>A collection of resource type names.</returns>
        public IEnumerable<string> GetResourceTypes(ListedCapabilityStatement capabilityStatement)
        {
            EnsureArg.IsNotNull(capabilityStatement, nameof(capabilityStatement));

            var resourceTypes = new List<string>();

            foreach (var rest in capabilityStatement.Rest)
            {
                foreach (var resource in rest.Resource)
                {
                    if (!string.IsNullOrWhiteSpace(resource.Type))
                    {
                        resourceTypes.Add(resource.Type);
                    }
                }
            }

            return resourceTypes.Distinct();
        }

        /// <summary>
        /// Extracts search parameters for a specific resource type.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <param name="resourceType">The resource type to get search parameters for.</param>
        /// <returns>A collection of search parameter information.</returns>
        public IEnumerable<SearchParameterInfo> GetSearchParametersForResource(
            ListedCapabilityStatement capabilityStatement,
            string resourceType)
        {
            EnsureArg.IsNotNull(capabilityStatement, nameof(capabilityStatement));
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            var searchParams = new List<SearchParameterInfo>();

            foreach (var rest in capabilityStatement.Rest)
            {
                var resource = rest.Resource.FirstOrDefault(r =>
                    string.Equals(r.Type, resourceType, StringComparison.OrdinalIgnoreCase));

                if (resource != null)
                {
                    foreach (var searchParam in resource.SearchParam)
                    {
                        searchParams.Add(new SearchParameterInfo(
                            searchParam.Name,
                            searchParam.Type,
                            searchParam.Definition?.ToString(),
                            searchParam.Documentation));
                    }
                }
            }

            return searchParams;
        }

        /// <summary>
        /// Extracts all search parameters across all resources.
        /// </summary>
        /// <param name="capabilityStatement">The capability statement to extract from.</param>
        /// <returns>A dictionary mapping resource types to their search parameters.</returns>
        public IDictionary<string, IEnumerable<SearchParameterInfo>> GetAllSearchParameters(
            ListedCapabilityStatement capabilityStatement)
        {
            EnsureArg.IsNotNull(capabilityStatement, nameof(capabilityStatement));

            var result = new Dictionary<string, IEnumerable<SearchParameterInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var resourceType in GetResourceTypes(capabilityStatement))
            {
                result[resourceType] = GetSearchParametersForResource(capabilityStatement, resourceType);
            }

            return result;
        }

        /// <summary>
        /// Represents information about a search parameter.
        /// </summary>
        public class SearchParameterInfo
        {
            public SearchParameterInfo(string name, SearchParamType type, string definition, string documentation)
            {
                Name = name;
                Type = type;
                Definition = definition;
                Documentation = documentation;
            }

            /// <summary>
            /// Gets the name of the search parameter.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the type of the search parameter.
            /// </summary>
            public SearchParamType Type { get; }

            /// <summary>
            /// Gets the definition URL of the search parameter.
            /// </summary>
            public string Definition { get; }

            /// <summary>
            /// Gets the documentation for the search parameter.
            /// </summary>
            public string Documentation { get; }
        }
    }
}

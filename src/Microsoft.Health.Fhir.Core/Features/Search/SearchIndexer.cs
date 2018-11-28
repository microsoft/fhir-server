// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides a mechanism to create search indices.
    /// </summary>
    public class SearchIndexer : ISearchIndexer
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IFhirElementToSearchValueTypeConverterManager _fhirElementTypeConverterManager;
        private readonly ILogger<SearchIndexer> _logger;

        private ConcurrentDictionary<string, List<string>> _targetTypesLookup = new ConcurrentDictionary<string, List<string>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchIndexer"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManager">The search parameter definition manager.</param>
        /// <param name="fhirElementTypeConverterManager">The FHIR element type converter manager.</param>
        /// <param name="logger">The logger.</param>
        public SearchIndexer(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IFhirElementToSearchValueTypeConverterManager fhirElementTypeConverterManager,
            ILogger<SearchIndexer> logger)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(fhirElementTypeConverterManager, nameof(fhirElementTypeConverterManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _fhirElementTypeConverterManager = fhirElementTypeConverterManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<SearchIndexEntry> Extract(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var entries = new List<SearchIndexEntry>();

            var context = new FhirEvaluationContext(resource);

            IEnumerable<SearchParameter> searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.ResourceType);

            foreach (SearchParameter searchParameter in searchParameters)
            {
                if (searchParameter.Name == SearchParameterNames.ResourceType)
                {
                    // We don't index the resource type value. We just use the property on the root document.

                    continue;
                }

                if (searchParameter.Type == SearchParamType.Composite)
                {
                    entries.AddRange(ProcessCompositeSearchParameter(searchParameter, resource, context));
                }
                else
                {
                    entries.AddRange(ProcessNonCompositeSearchParameter(searchParameter, resource, context));
                }
            }

            return entries;
        }

        private IEnumerable<SearchIndexEntry> ProcessCompositeSearchParameter(SearchParameter searchParameter, Resource resource, FhirEvaluationContext context)
        {
            Debug.Assert(searchParameter?.Type == SearchParamType.Composite, "The search parameter must be composite.");

            Base[] rootObjects = resource.Select(searchParameter.Expression, context).ToArray();

            foreach (Base rootObject in rootObjects)
            {
                int numberOfComponents = searchParameter.Component.Count;
                bool skip = false;

                var componentValues = new IReadOnlyList<ISearchValue>[numberOfComponents];

                // For each object extracted from the expression, we will need to evaluate each component.
                for (int i = 0; i < numberOfComponents; i++)
                {
                    SearchParameter.ComponentComponent component = searchParameter.Component[i];

                    // First find the type of the component.
                    SearchParameter componentSearchParameterDefinition = _searchParameterDefinitionManager.GetSearchParameter(component.Definition.Url);

                    IReadOnlyList<ISearchValue> extractedComponentValues = ExtractSearchValues(
                        componentSearchParameterDefinition.Url,
                        componentSearchParameterDefinition.Type.Value,
                        componentSearchParameterDefinition.Target,
                        rootObject,
                        component.Expression,
                        context);

                    // Filter out any search value that's not valid as a composite component.
                    extractedComponentValues = extractedComponentValues
                        .Where(sv => sv.IsValidAsCompositeComponent)
                        .ToArray();

                    if (!extractedComponentValues.Any())
                    {
                        // One of the components didn't have any value and therefore it will not be indexed.
                        skip = true;
                        break;
                    }

                    componentValues[i] = extractedComponentValues;
                }

                if (skip)
                {
                    continue;
                }

                yield return new SearchIndexEntry(searchParameter.Name, new CompositeSearchValue(componentValues));
            }
        }

        private IEnumerable<SearchIndexEntry> ProcessNonCompositeSearchParameter(SearchParameter searchParameter, Resource resource, FhirEvaluationContext context)
        {
            Debug.Assert(searchParameter?.Type != SearchParamType.Composite, "The search parameter must be non-composite.");

            foreach (ISearchValue searchValue in ExtractSearchValues(
                searchParameter.Url,
                searchParameter.Type.Value,
                searchParameter.Target,
                resource,
                searchParameter.Expression,
                context))
            {
                yield return new SearchIndexEntry(searchParameter.Name, searchValue);
            }
        }

        private IReadOnlyList<ISearchValue> ExtractSearchValues(
            string searchParameterDefinitionUrl,
            SearchParamType searchParameterType,
            IEnumerable<ResourceType?> allowedReferenceResourceTypes,
            Base element,
            string fhirPathExpression,
            FhirEvaluationContext context)
        {
            Debug.Assert(searchParameterType != SearchParamType.Composite, "The search parameter must be non-composite.");

            var results = new List<ISearchValue>();

            // For simple value type, we can parse the expression directly.
            IEnumerable<Base> extractedValues = Enumerable.Empty<Base>();

            try
            {
                extractedValues = element.Select(fhirPathExpression, context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to extract the values using '{FhirPathExpression}' against '{ElementType}'.",
                    fhirPathExpression,
                    element.GetType());
            }

            Debug.Assert(extractedValues != null, "The extracted values should not be null.");

            // If there is target set, then filter the extracted values to only those types.
            if (searchParameterType == SearchParamType.Reference &&
                allowedReferenceResourceTypes != null &&
                allowedReferenceResourceTypes.Any())
            {
                List<string> targetResourceTypes = _targetTypesLookup.GetOrAdd(searchParameterDefinitionUrl, _ =>
                {
                    return allowedReferenceResourceTypes.Select(t => t.ToString()).ToList();
                });

                // TODO: The expression for reference search parameters in STU3 has issues.
                // The reference search parameter could be pointing to an element that can be multiple types. For example,
                // the Appointment.participant.actor can be type of Patient, Practitioner, Related Person, Location, and so on.
                // Some search parameter could refer to this property but restrict to certain types. For example,
                // Appointment's location search parameter is returned only when Appointment.participant.actor is Location element.
                // The STU3 expressions don't have this restriction so everything is being returned. This is addressed in R4 release (see
                // http://community.fhir.org/t/expression-seems-incorrect-for-reference-search-parameter-thats-only-applicable-to-certain-types/916/2).
                // Therefore, for now, we will need to compare the reference value itself (which can be internal or external references), and restrict
                // the values ourselves.
                extractedValues = extractedValues.Where(ev =>
                    ev is ResourceReference rr &&
                    rr.Reference != null &&
                    targetResourceTypes.Any(trt => rr.Reference.Contains(trt, StringComparison.Ordinal)));
            }

            foreach (Base extractedValue in extractedValues)
            {
                if (!_fhirElementTypeConverterManager.TryGetConverter(extractedValue.GetType(), out IFhirElementToSearchValueTypeConverter converter))
                {
                    _logger.LogWarning(
                        "The FHIR element '{ElementType}' is not supported.",
                        extractedValue.TypeName);

                    continue;
                }

                _logger.LogDebug(
                    "The FHIR element '{ElementType}' will be converted using '{ElementTypeConverter}'.",
                    extractedValue.TypeName,
                    converter.GetType().FullName);

                results.AddRange(converter.ConvertTo(extractedValue) ?? Enumerable.Empty<ISearchValue>());
            }

            return results;
        }
    }
}

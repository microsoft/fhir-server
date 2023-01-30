// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal static class SearchParameterDefinitionBuilder
    {
        private static readonly ISet<Uri> _missingExpressionsInR5 = new HashSet<Uri>
        {
            new("http://hl7.org/fhir/SearchParameter/EvidenceVariable-topic"),
            new("http://hl7.org/fhir/SearchParameter/ImagingStudy-reason"),
            new("http://hl7.org/fhir/SearchParameter/Medication-form"),
            new("http://hl7.org/fhir/SearchParameter/MedicationKnowledge-packaging-cost"),
            new("http://hl7.org/fhir/SearchParameter/MedicationKnowledge-packaging-cost-concept"),
            new("http://hl7.org/fhir/SearchParameter/Subscription-payload"),
            new("http://hl7.org/fhir/SearchParameter/Subscription-type"),
            new("http://hl7.org/fhir/SearchParameter/Subscription-url"),
            new("http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-conformance"),
            new("http://hl7.org/fhir/SearchParameter/TestScript-scope-artifact-phase"),
        };

        internal static void Build(
            IReadOnlyCollection<ITypedElement> searchParameters,
            ConcurrentDictionary<string, SearchParameterInfo> uriDictionary,
            ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> resourceTypeDictionary,
            IModelInfoProvider modelInfoProvider,
            ILogger logger)
        {
            EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));
            EnsureArg.IsNotNull(uriDictionary, nameof(uriDictionary));
            EnsureArg.IsNotNull(resourceTypeDictionary, nameof(resourceTypeDictionary));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            ILookup<string, SearchParameterInfo> searchParametersLookup = ValidateAndGetFlattenedList(
                searchParameters,
                uriDictionary,
                modelInfoProvider).ToLookup(
                    entry => entry.ResourceType,
                    entry => entry.SearchParameter);

            // Build the inheritance. For example, the _id search parameter is on Resource
            // and should be available to all resources that inherit Resource.
            foreach (string resourceType in modelInfoProvider.GetResourceTypeNames())
            {
                // Recursively build the search parameter definitions. For example,
                // Appointment inherits from DomainResource, which inherits from Resource
                // and therefore Appointment should include all search parameters DomainResource and Resource supports.
                BuildSearchParameterDefinition(searchParametersLookup, resourceType, resourceTypeDictionary, modelInfoProvider, logger);
            }
        }

        private static bool ShouldExcludeEntry(string resourceType, string searchParameterName, IModelInfoProvider modelInfoProvider)
        {
            return (resourceType == KnownResourceTypes.DomainResource && searchParameterName == "_text") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_text") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_content") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_query") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_list") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_has") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_filter") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_type") ||
                   ShouldExcludeEntryStu3(resourceType, searchParameterName, modelInfoProvider);
        }

        private static bool ShouldExcludeEntryStu3(string resourceType, string searchParameterName, IModelInfoProvider modelInfoProvider)
        {
            return modelInfoProvider.Version == FhirSpecification.Stu3 &&
                   resourceType == "DataElement" && (searchParameterName == "objectClass" || searchParameterName == "objectClassProperty");
        }

        internal static BundleWrapper ReadEmbeddedSearchParameters(
            string embeddedResourceName,
            IModelInfoProvider modelInfoProvider,
            string embeddedResourceNamespace = null,
            Assembly assembly = null)
        {
            using Stream stream = modelInfoProvider.OpenVersionedFileStream(embeddedResourceName, embeddedResourceNamespace, assembly);

            using TextReader reader = new StreamReader(stream);
            var data = reader.ReadToEnd();
            var rawResource = new RawResource(data, FhirResourceFormat.Json, true);

            return new BundleWrapper(modelInfoProvider.ToTypedElement(rawResource));
        }

        private static SearchParameterInfo GetOrCreateSearchParameterInfo(SearchParameterWrapper searchParameter, IDictionary<string, SearchParameterInfo> uriDictionary)
        {
            // Return SearchParameterInfo that has already been created for this Uri
            if (uriDictionary.TryGetValue(searchParameter.Url, out var spi))
            {
                return spi;
            }

            return new SearchParameterInfo(searchParameter);
        }

        private static List<(string ResourceType, SearchParameterInfo SearchParameter)> ValidateAndGetFlattenedList(
            IReadOnlyCollection<ITypedElement> searchParamCollection,
            IDictionary<string, SearchParameterInfo> uriDictionary,
            IModelInfoProvider modelInfoProvider)
        {
            var issues = new List<OperationOutcomeIssue>();
            var searchParameters = searchParamCollection.Select((x, entryIndex) =>
            {
                try
                {
                    return new SearchParameterWrapper(x);
                }
                catch (ArgumentException)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionInvalidResource, entryIndex);
                    return null;
                }
            }).ToList();

            // Do the first pass to make sure all resources are SearchParameter.
            for (int entryIndex = 0; entryIndex < searchParameters.Count; entryIndex++)
            {
                SearchParameterWrapper searchParameter = searchParameters[entryIndex];

                if (searchParameter == null)
                {
                    continue;
                }

                try
                {
                    SearchParameterInfo searchParameterInfo = GetOrCreateSearchParameterInfo(searchParameter, uriDictionary);

                    if (searchParameterInfo.Code == "_profile" && searchParameterInfo.Type == SearchParamType.Reference)
                    {
                        // _profile can't be handled as a reference since it points to an external permalink
                        searchParameterInfo.Type = SearchParamType.Uri;
                    }

                    uriDictionary.Add(searchParameter.Url, searchParameterInfo);
                }
                catch (FormatException)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionInvalidDefinitionUri, entryIndex);
                    continue;
                }
                catch (ArgumentException)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionDuplicatedEntry, searchParameter.Url);
                    continue;
                }
            }

            EnsureNoIssues();

            var validatedSearchParameters = new List<(string ResourceType, SearchParameterInfo SearchParameter)>
            {
                // _type is currently missing from the search params definition bundle, so we inject it in here.
                (KnownResourceTypes.Resource, SearchParameterInfo.ResourceTypeSearchParameter),
            };

            // Do the second pass to make sure the definition is valid.
            foreach (var searchParameter in searchParameters)
            {
                if (searchParameter == null)
                {
                    continue;
                }

                // If this is a composite search parameter, then make sure components are defined.
                if (string.Equals(searchParameter.Type, SearchParamType.Composite.GetLiteral(), StringComparison.OrdinalIgnoreCase))
                {
                    if (modelInfoProvider.Version == FhirSpecification.R5 && _missingExpressionsInR5.Contains(new Uri(searchParameter.Url)))
                    {
                        continue;
                    }

                    var composites = searchParameter.Component;
                    if (composites.Count == 0)
                    {
                        AddIssue(Core.Resources.SearchParameterDefinitionInvalidComponent, searchParameter.Url);
                        continue;
                    }

                    SearchParameterInfo compositeSearchParameter = GetOrCreateSearchParameterInfo(searchParameter, uriDictionary);

                    for (int componentIndex = 0; componentIndex < composites.Count; componentIndex++)
                    {
                        ITypedElement component = composites[componentIndex];
                        var definitionUrl = GetComponentDefinition(component);

                        if (definitionUrl == null ||
                            !uriDictionary.TryGetValue(definitionUrl, out SearchParameterInfo componentSearchParameter))
                        {
                            AddIssue(
                                Core.Resources.SearchParameterDefinitionInvalidComponentReference,
                                searchParameter.Url,
                                componentIndex);
                            continue;
                        }

                        if (componentSearchParameter.Type == SearchParamType.Composite)
                        {
                            AddIssue(
                                Core.Resources.SearchParameterDefinitionComponentReferenceCannotBeComposite,
                                searchParameter.Url,
                                componentIndex);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(component.Scalar("expression")?.ToString()))
                        {
                            AddIssue(
                                Core.Resources.SearchParameterDefinitionInvalidComponentExpression,
                                searchParameter.Url,
                                componentIndex);
                            continue;
                        }

                        compositeSearchParameter.Component[componentIndex].ResolvedSearchParameter = componentSearchParameter;
                    }
                }

                // Make sure the base is defined.
                IReadOnlyList<string> bases = searchParameter.Base;
                if (bases.Count == 0)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionBaseNotDefined, searchParameter.Url);
                    continue;
                }

                for (int baseElementIndex = 0; baseElementIndex < bases.Count; baseElementIndex++)
                {
                    var code = bases[baseElementIndex];

                    string baseResourceType = code;

                    // Make sure the expression is not empty unless they are known to have empty expression.
                    // These are special search parameters that searches across all properties and needs to be handled specially.
                    if (ShouldExcludeEntry(baseResourceType, searchParameter.Name, modelInfoProvider)
                    || (modelInfoProvider.Version == FhirSpecification.R5 && _missingExpressionsInR5.Contains(new Uri(searchParameter.Url))))
                    {
                        continue;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(searchParameter.Expression))
                        {
                            AddIssue(Core.Resources.SearchParameterDefinitionInvalidExpression, searchParameter.Url);
                            continue;
                        }
                    }

                    validatedSearchParameters.Add((baseResourceType, GetOrCreateSearchParameterInfo(searchParameter, uriDictionary)));
                }
            }

            EnsureNoIssues();

            return validatedSearchParameters;

            void AddIssue(string format, params object[] args)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Fatal,
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, format, args)));
            }

            void EnsureNoIssues()
            {
                if (issues.Count != 0)
                {
                    throw new InvalidDefinitionException(
                        Core.Resources.SearchParameterDefinitionContainsInvalidEntry,
                        issues.ToArray());
                }
            }
        }

        private static HashSet<SearchParameterInfo> BuildSearchParameterDefinition(
            ILookup<string, SearchParameterInfo> searchParametersLookup,
            string resourceType,
            ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> resourceTypeDictionary,
            IModelInfoProvider modelInfoProvider,
            ILogger logger)
        {
            HashSet<SearchParameterInfo> results;
            if (resourceTypeDictionary.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> cachedSearchParameters))
            {
                results = new HashSet<SearchParameterInfo>(cachedSearchParameters.Values);
            }
            else
            {
                results = new HashSet<SearchParameterInfo>();
            }

            Type type = modelInfoProvider.GetTypeForFhirType(resourceType);

            Debug.Assert(type != null, $"The type for {resourceType} should not be null.");

            string baseType = modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

            if (baseType != null && !string.Equals(KnownResourceTypes.Base, baseType, StringComparison.OrdinalIgnoreCase))
            {
                HashSet<SearchParameterInfo> baseResults = BuildSearchParameterDefinition(searchParametersLookup, baseType, resourceTypeDictionary, modelInfoProvider, logger);
                results.UnionWith(baseResults);
            }

            results.UnionWith(searchParametersLookup[resourceType]);

            var searchParameterDictionary = new ConcurrentDictionary<string, SearchParameterInfo>();
            foreach (SearchParameterInfo searchParam in results)
            {
                if (!searchParameterDictionary.TryAdd(searchParam.Code, searchParam) && searchParameterDictionary.TryGetValue(searchParam.Code, out SearchParameterInfo searchPWithSameCode))
                {
                    logger.LogWarning("SearchParameterDefinitionBuilder: Search parameter name {SearchParam1} with Base Resource Type {BaseResourceTypes1} has same code {Code} as Search Param name {SearchParam2} with Base Resource Type {BaseResourceTypes2}", searchParam.Name, searchParam.BaseResourceTypes, searchParam.Code, searchPWithSameCode.Name, searchPWithSameCode.BaseResourceTypes);
                }
            }

            if (!resourceTypeDictionary.TryAdd(resourceType, searchParameterDictionary))
            {
                resourceTypeDictionary[resourceType] = searchParameterDictionary;
            }

            return results;
        }

        private static string GetComponentDefinition(ITypedElement component)
        {
            // In Stu3 the Url is under 'definition.reference'
            return component.Scalar("definition.reference")?.ToString() ??
                   component.Scalar("definition")?.ToString();
        }
    }
}

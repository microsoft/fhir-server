// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal class SearchParameterDefinitionBuilder
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly Assembly _assembly;
        private readonly string _embeddedResourceName;
        private readonly string _embeddedResourceNamespace;

        private readonly Dictionary<Uri, SearchParameterInfo> _uriDictionary = new Dictionary<Uri, SearchParameterInfo>();
        private readonly Dictionary<string, IDictionary<string, SearchParameterInfo>> _resourceTypeDictionary = new Dictionary<string, IDictionary<string, SearchParameterInfo>>();

        private bool _initialized;

        private readonly ISet<Uri> _knownBrokenR5 = new HashSet<Uri>
        {
            new Uri("http://hl7.org/fhir/SearchParameter/Subscription-url"),
            new Uri("http://hl7.org/fhir/SearchParameter/ImagingStudy-reason"),
            new Uri("http://hl7.org/fhir/SearchParameter/Medication-form"),
            new Uri("http://hl7.org/fhir/SearchParameter/PackagedProductDefinition-device"),
            new Uri("http://hl7.org/fhir/SearchParameter/PackagedProductDefinition-manufactured-item"),
            new Uri("http://hl7.org/fhir/SearchParameter/Subscription-payload"),
            new Uri("http://hl7.org/fhir/SearchParameter/Subscription-type"),
        };

        internal SearchParameterDefinitionBuilder(
            IModelInfoProvider modelInfoProvider,
            string embeddedResourceName,
            string embeddedResourceNamespace = null,
            Assembly assembly = null)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNullOrWhiteSpace(embeddedResourceName, nameof(embeddedResourceName));

            _modelInfoProvider = modelInfoProvider;
            _assembly = assembly;
            _embeddedResourceNamespace = embeddedResourceNamespace;
            _embeddedResourceName = embeddedResourceName;
        }

        internal IDictionary<Uri, SearchParameterInfo> UriDictionary
        {
            get
            {
                if (!_initialized)
                {
                    Build();
                }

                return _uriDictionary;
            }
        }

        internal IDictionary<string, IDictionary<string, SearchParameterInfo>> ResourceTypeDictionary
        {
            get
            {
                if (!_initialized)
                {
                    Build();
                }

                return _resourceTypeDictionary;
            }
        }

        internal void Build()
        {
            ILookup<string, SearchParameterInfo> searchParametersLookup = ValidateAndGetFlattenedList()
                .ToLookup(
                    entry => entry.ResourceType,
                    entry => entry.SearchParameter);

            // Build the inheritance. For example, the _id search parameter is on Resource
            // and should be available to all resources that inherit Resource.
            foreach (string resourceType in _modelInfoProvider.GetResourceTypeNames())
            {
                if (_resourceTypeDictionary.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> _))
                {
                    // The list has already been built, move on.
                    continue;
                }

                // Recursively build the search parameter definitions. For example,
                // Appointment inherits from DomainResource, which inherits from Resource
                // and therefore Appointment should include all search parameters DomainResource and Resource supports.
                BuildSearchParameterDefinition(searchParametersLookup, resourceType);
            }

            _initialized = true;
        }

        private bool ShouldExcludeEntry(string resourceType, string searchParameterName)
        {
            return (resourceType == KnownResourceTypes.DomainResource && searchParameterName == "_text") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_content") ||
                   (resourceType == KnownResourceTypes.Resource && searchParameterName == "_query") ||
                   ShouldExcludeEntryStu3(resourceType, searchParameterName);
        }

        private bool ShouldExcludeEntryStu3(string resourceType, string searchParameterName)
        {
            return _modelInfoProvider.Version == FhirSpecification.Stu3 &&
                   resourceType == "DataElement" && (searchParameterName == "objectClass" || searchParameterName == "objectClassProperty");
        }

        private List<(string ResourceType, SearchParameterInfo SearchParameter)> ValidateAndGetFlattenedList()
        {
            var issues = new List<OperationOutcomeIssue>();

            BundleWrapper bundle = null;

            using (Stream stream = _modelInfoProvider.OpenVersionedFileStream(_embeddedResourceName, _embeddedResourceNamespace, _assembly))
            {
                using TextReader reader = new StreamReader(stream);
                using JsonReader jsonReader = new JsonTextReader(reader);
                try
                {
                    bundle = new BundleWrapper(FhirJsonNode.Read(jsonReader).ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider));
                }
                catch (FormatException ex)
                {
                    AddIssue(ex.Message);
                }
            }

            EnsureNoIssues();

            IReadOnlyList<BundleEntryWrapper> entries = bundle.Entries;

            // Do the first pass to make sure all resources are SearchParameter.
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                // Make sure resources are not null and they are SearchParameter.
                BundleEntryWrapper entry = entries[entryIndex];

                ITypedElement searchParameterElement = entry.Resource;

                if (searchParameterElement == null || !string.Equals(searchParameterElement.InstanceType, KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionInvalidResource, entryIndex);
                    continue;
                }

                var searchParameter = new SearchParameterWrapper(searchParameterElement);

                try
                {
                    SearchParameterInfo searchParameterInfo = CreateSearchParameterInfo(searchParameter);
                    _uriDictionary.Add(new Uri(searchParameter.Url), searchParameterInfo);
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
                (KnownResourceTypes.Resource, new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParamType.Token, SearchParameterNames.ResourceTypeUri, null, "Resource.type().name", null)),
            };

            // Do the second pass to make sure the definition is valid.
            for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                BundleEntryWrapper entry = entries[entryIndex];

                ITypedElement searchParameterElement = entry.Resource;
                var searchParameter = new SearchParameterWrapper(searchParameterElement);

                // If this is a composite search parameter, then make sure components are defined.
                if (string.Equals(searchParameter.Type, SearchParamType.Composite.GetLiteral(), StringComparison.OrdinalIgnoreCase))
                {
                    var composites = searchParameter.Component;
                    if (composites.Count == 0)
                    {
                        AddIssue(Core.Resources.SearchParameterDefinitionInvalidComponent, searchParameter.Url);
                        continue;
                    }

                    for (int componentIndex = 0; componentIndex < composites.Count; componentIndex++)
                    {
                        ITypedElement component = composites[componentIndex];
                        var definitionUrl = GetComponentDefinition(component);

                        if (definitionUrl == null ||
                            !_uriDictionary.TryGetValue(new Uri(definitionUrl), out SearchParameterInfo componentSearchParameter))
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
                    }
                }

                // Make sure the base is defined.
                var bases = searchParameter.Base;
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
                    if (ShouldExcludeEntry(baseResourceType, searchParameter.Name)
                    || (_modelInfoProvider.Version == FhirSpecification.R5 && _knownBrokenR5.Contains(new Uri(searchParameter.Url))))
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

                    validatedSearchParameters.Add((baseResourceType, CreateSearchParameterInfo(searchParameter)));
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

        private SearchParameterInfo CreateSearchParameterInfo(SearchParameterWrapper searchParameter)
        {
            // Return SearchParameterInfo that has already been created for this Uri
            if (_uriDictionary.TryGetValue(new Uri(searchParameter.Url), out var spi))
            {
                return spi;
            }

            var components = searchParameter.Component
                .Select(x => new SearchParameterComponentInfo(
                    new Uri(GetComponentDefinition(x)),
                    x.Scalar("expression")?.ToString()))
                .ToArray();

            SearchParamType searchParamType = EnumUtility.ParseLiteral<SearchParamType>(searchParameter.Type)
                .GetValueOrDefault();

            var sp = new SearchParameterInfo(
                searchParameter.Name,
                searchParamType,
                new Uri(searchParameter.Url),
                expression: searchParameter.Expression,
                description: searchParameter.Description,
                components: components,
                targetResourceTypes: searchParameter.Target,
                baseResourceTypes: searchParameter.Base);

            return sp;
        }

        private IEnumerable<SearchParameterInfo> BuildSearchParameterDefinition(
            ILookup<string, SearchParameterInfo> searchParametersLookup,
            string resourceType)
        {
            if (_resourceTypeDictionary.TryGetValue(resourceType, out IDictionary<string, SearchParameterInfo> cachedSearchParameters))
            {
                return cachedSearchParameters.Values;
            }

            IEnumerable<SearchParameterInfo> results = Enumerable.Empty<SearchParameterInfo>();

            Type type = _modelInfoProvider.GetTypeForFhirType(resourceType);

            Debug.Assert(type != null, $"The type for {resourceType} should not be null.");

            string baseType = _modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

            if (baseType != null)
            {
                results = BuildSearchParameterDefinition(searchParametersLookup, baseType);
            }

            Debug.Assert(results != null, "The results should not be null.");

            results = results.Concat(searchParametersLookup[resourceType]);

            Dictionary<string, SearchParameterInfo> searchParameterDictionary = results.ToDictionary(
                r => r.Name,
                r => r,
                StringComparer.Ordinal);

            _resourceTypeDictionary.Add(resourceType, searchParameterDictionary);

            return searchParameterDictionary.Values;
        }

        private static string GetComponentDefinition(ITypedElement component)
        {
            // In Stu3 the Url is under 'definition.reference'
            return component.Scalar("definition.reference")?.ToString() ??
                   component.Scalar("definition")?.ToString();
        }
    }
}

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
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.SearchParameter;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal class SearchParameterDefinitionBuilder
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly Assembly _assembly;
        private readonly string _embeddedResourceName;

        private readonly Dictionary<Uri, SearchParameterInfo> _uriDictionary = new Dictionary<Uri, SearchParameterInfo>();
        private readonly Dictionary<string, IDictionary<string, SearchParameterInfo>> _resourceTypeDictionary = new Dictionary<string, IDictionary<string, SearchParameterInfo>>();

        private bool _initialized;

        internal SearchParameterDefinitionBuilder(
            FhirJsonParser fhirJsonParser,
            IModelInfoProvider modelInfoProvider,
            Assembly assembly,
            string embeddedResourceName)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(assembly, nameof(assembly));
            EnsureArg.IsNotNullOrWhiteSpace(embeddedResourceName, nameof(embeddedResourceName));

            _fhirJsonParser = fhirJsonParser;
            _modelInfoProvider = modelInfoProvider;
            _assembly = assembly;
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
            ILookup<string, SearchParameterInfo> searchParametersLookup = ValidateAndGetFlattendList()
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

        private static bool ShouldExcludeEntry(string resourceType, string searchParameterName)
        {
            return (resourceType == ResourceType.DomainResource.ToString() && searchParameterName == "_text") ||
                   (resourceType == ResourceType.Resource.ToString() && searchParameterName == "_content") ||
                   (resourceType == ResourceType.Resource.ToString() && searchParameterName == "_query")
#if Stu3
                || (resourceType == ResourceType.DataElement.ToString() && (searchParameterName == "objectClass" || searchParameterName == "objectClassProperty"))
#endif
                ;
        }

        private List<(string ResourceType, SearchParameterInfo SearchParameter)> ValidateAndGetFlattendList()
        {
            var issues = new List<OperationOutcomeIssue>();

            Bundle bundle = null;

            using (Stream stream = _assembly.GetManifestResourceStream(_embeddedResourceName))
            using (TextReader reader = new StreamReader(stream))
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                try
                {
                    // The parser does some basic validation such as making sure entry is not null, enum has the right value, and etc.
                    bundle = _fhirJsonParser.Parse<Bundle>(jsonReader);
                }
                catch (FormatException ex)
                {
                    AddIssue(ex.Message);
                }
            }

            EnsureNoIssues();

            // Do the first pass to make sure all resources are SearchParameter.
            for (int entryIndex = 0; entryIndex < bundle.Entry.Count; entryIndex++)
            {
                // Make sure resources are not null and they are SearchParameter.
                EntryComponent entry = bundle.Entry[entryIndex];

                var searchParameter = entry.Resource as SearchParameter;

                if (searchParameter == null)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionInvalidResource, entryIndex);
                    continue;
                }

                try
                {
                    _uriDictionary.Add(new Uri(searchParameter.Url), searchParameter.ToInfo());
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
                (ResourceType.Resource.ToString(), new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParamType.Token.ToString(), SearchParameterNames.ResourceTypeUri, null, "Resource.type().name", null)),
            };

            // Do the second pass to make sure the definition is valid.
            for (int entryIndex = 0; entryIndex < bundle.Entry.Count; entryIndex++)
            {
                var searchParameter = (SearchParameter)bundle.Entry[entryIndex].Resource;

                // If this is a composite search parameter, then make sure components are defined.
                if (searchParameter.Type == SearchParamType.Composite)
                {
                    if (searchParameter.Component?.Count == 0)
                    {
                        AddIssue(Core.Resources.SearchParameterDefinitionInvalidComponent, searchParameter.Url);
                        continue;
                    }

                    for (int componentIndex = 0; componentIndex < searchParameter.Component.Count; componentIndex++)
                    {
                        ComponentComponent component = searchParameter.Component[componentIndex];

                        if (component.GetComponentDefinitionUri() == null ||
                            !_uriDictionary.TryGetValue(component.GetComponentDefinitionUri(), out SearchParameterInfo componentSearchParameter))
                        {
                            AddIssue(
                                Core.Resources.SearchParameterDefinitionInvalidComponentReference,
                                searchParameter.Url,
                                componentIndex);
                            continue;
                        }

                        if (componentSearchParameter.Type == SearchParamType.Composite.ToValueSet())
                        {
                            AddIssue(
                                Core.Resources.SearchParameterDefinitionComponentReferenceCannotBeComposite,
                                searchParameter.Url,
                                componentIndex);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(component.Expression))
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
                if (searchParameter.BaseElement?.Count == 0)
                {
                    AddIssue(Core.Resources.SearchParameterDefinitionBaseNotDefined, searchParameter.Url);
                    continue;
                }

                for (int baseElementIndex = 0; baseElementIndex < searchParameter.BaseElement.Count; baseElementIndex++)
                {
                    Code<ResourceType> code = searchParameter.BaseElement[baseElementIndex];

                    string baseResourceType = code.Value.Value.ToString();

                    // Make sure the expression is not empty unless they are known to have empty expression.
                    // These are special search parameters that searches across all properties and needs to be handled specially.
                    if (ShouldExcludeEntry(baseResourceType, searchParameter.Name))
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

                    validatedSearchParameters.Add((baseResourceType, searchParameter.ToInfo()));
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

            Debug.Assert(type != null, "The type should not be null.");

            string baseType = _modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

            if (baseType != null && Enum.TryParse(baseType, out ResourceType baseResourceType))
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
    }
}

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
        private readonly Assembly _assembly;
        private readonly string _embeddedResourceName;

        private Dictionary<Uri, SearchParameter> _uriDictionary = new Dictionary<Uri, SearchParameter>();
        private Dictionary<string, IDictionary<string, SearchParameter>> _resourceTypeDictionary = new Dictionary<string, IDictionary<string, SearchParameter>>();

        private bool _initialized;

        internal SearchParameterDefinitionBuilder(FhirJsonParser fhirJsonParser, Assembly assembly, string embeddedResourceName)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(assembly, nameof(assembly));
            EnsureArg.IsNotNullOrWhiteSpace(embeddedResourceName, nameof(embeddedResourceName));

            _fhirJsonParser = fhirJsonParser;
            _assembly = assembly;
            _embeddedResourceName = embeddedResourceName;
        }

        internal IDictionary<Uri, SearchParameter> UriDictionary
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

        internal IDictionary<string, IDictionary<string, SearchParameter>> ResourceTypeDictionary
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
            ILookup<string, SearchParameter> searchParametersLookup = ValidateAndGetFlattendList()
                .ToLookup(
                    entry => entry.ResourceType,
                    entry => entry.SearchParameter);

            // Build the inheritance. For example, the _id search parameter is on Resource
            // and should be available to all resources that inherit Resource.
            foreach (IGrouping<string, SearchParameter> entry in searchParametersLookup)
            {
                if (_resourceTypeDictionary.TryGetValue(entry.Key, out IDictionary<string, SearchParameter> _))
                {
                    // The list has already been built, move on.
                    continue;
                }

                // Recursively build the search parameter definitions. For example,
                // Appointment inherits from DomainResource, which inherits from Resource
                // and therefore Appointment should include all search parameters DomainResource and Resource supports.
                BuildSearchParameterDefinition(searchParametersLookup, entry.Key);
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

        private List<(string ResourceType, SearchParameter SearchParameter)> ValidateAndGetFlattendList()
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
                    _uriDictionary.Add(new Uri(searchParameter.Url), searchParameter);
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

            var validatedSearchParameters = new List<(string ResourceType, SearchParameter SearchParameter)>
            {
                // _type is currently missing from the search params definition bundle, so we inject it in here.
                (ResourceType.Resource.ToString(), new SearchParameter { Name = SearchParameterNames.ResourceType, Expression = "Resource.type().name", Type = SearchParamType.Token }),
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
                            !_uriDictionary.TryGetValue(component.GetComponentDefinitionUri(), out SearchParameter componentSearchParameter))
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

                    validatedSearchParameters.Add((baseResourceType, searchParameter));
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

        private IEnumerable<SearchParameter> BuildSearchParameterDefinition(
            ILookup<string, SearchParameter> searchParametersLookup,
            string resourceType)
        {
            if (_resourceTypeDictionary.TryGetValue(resourceType, out IDictionary<string, SearchParameter> cachedSearchParameters))
            {
                return cachedSearchParameters.Values;
            }

            IEnumerable<SearchParameter> results = Enumerable.Empty<SearchParameter>();

            Type type = ModelInfoProvider.GetTypeForFhirType(resourceType);

            Debug.Assert(type != null, "The type should not be null.");

            string baseType = ModelInfo.GetFhirTypeNameForType(type.BaseType);

            if (baseType != null && Enum.TryParse(baseType, out ResourceType baseResourceType))
            {
                results = BuildSearchParameterDefinition(searchParametersLookup, baseType);
            }

            Debug.Assert(results != null, "The results should not be null.");

            results = results.Concat(searchParametersLookup[resourceType]);

            Dictionary<string, SearchParameter> searchParameterDictionary = results.ToDictionary(
                r => r.Name,
                r => r,
                StringComparer.Ordinal);

            _resourceTypeDictionary.Add(resourceType, searchParameterDictionary);

            return searchParameterDictionary.Values;
        }
    }
}

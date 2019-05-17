// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.CompartmentDefinition;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Manager to access compartment definitions.
    /// </summary>
    public class CompartmentDefinitionManager : IStartable, ICompartmentDefinitionManager
    {
        private readonly FhirJsonParser _fhirJsonParser;

        // This data structure stores the lookup of compartmentsearchparams (in the hash set) by ResourceType and CompartmentType.
        private Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>> _compartmentSearchParamsLookup;

        public CompartmentDefinitionManager(FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            _fhirJsonParser = fhirJsonParser;
        }

        public static Dictionary<string, CompartmentType> ResourceTypeToCompartmentType { get; } = new Dictionary<string, CompartmentType>
        {
            { ResourceType.Device.ToString(), CompartmentType.Device },
            { ResourceType.Encounter.ToString(), CompartmentType.Encounter },
            { ResourceType.Patient.ToString(), CompartmentType.Patient },
            { ResourceType.Practitioner.ToString(), CompartmentType.Practitioner },
            { ResourceType.RelatedPerson.ToString(), CompartmentType.RelatedPerson },
        };

        public void Start()
        {
            Type type = GetType();

            // The json file is a bundle compiled from the compartment definitions currently defined by HL7.
            // The definitions are available at https://www.hl7.org/fhir/compartmentdefinition.html.
            using (Stream stream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.compartment.json"))
            using (TextReader reader = new StreamReader(stream))
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                Bundle bundle = _fhirJsonParser.Parse<Bundle>(jsonReader);
                Build(bundle);
            }
        }

        public bool TryGetSearchParams(string resourceType, string compartment, out HashSet<string> searchParams)
        {
            var resourceTypeEnum = Enum.Parse<ResourceType>(resourceType);

            if (_compartmentSearchParamsLookup.TryGetValue(resourceTypeEnum, out Dictionary<CompartmentType, HashSet<string>> compartmentSearchParams)
&& compartmentSearchParams.TryGetValue(Enum.Parse<CompartmentType>(compartment), out searchParams))
            {
                return true;
            }

            searchParams = null;
            return false;
        }

        public static ResourceType CompartmentTypeToResourceType(string compartmentType)
        {
            EnsureArg.IsTrue(Enum.IsDefined(typeof(CompartmentType), compartmentType), nameof(compartmentType));
            return ModelInfo.FhirTypeNameToResourceType(compartmentType).Value;
        }

        internal void Build(Bundle bundle)
        {
            var compartmentLookup = ValidateAndGetCompartmentDict(bundle);
            _compartmentSearchParamsLookup = BuildResourceTypeLookup(compartmentLookup.Values);
        }

        private static Dictionary<CompartmentType, CompartmentDefinition> ValidateAndGetCompartmentDict(Bundle bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));
            var issues = new List<OperationOutcomeIssue>();
            var validatedCompartments = new Dictionary<CompartmentType, CompartmentDefinition>();

            for (int entryIndex = 0; entryIndex < bundle.Entry.Count; entryIndex++)
            {
                // Make sure resources are not null and they are Compartment.
                EntryComponent entry = bundle.Entry[entryIndex];

                var compartment = entry.Resource as CompartmentDefinition;

                if (compartment == null)
                {
                    AddIssue(Core.Resources.CompartmentDefinitionInvalidResource, entryIndex);
                    continue;
                }

                if (compartment.Code == null)
                {
                    AddIssue(Core.Resources.CompartmentDefinitionInvalidCompartmentType, entryIndex);
                    continue;
                }

                if (validatedCompartments.ContainsKey(compartment.Code.Value))
                {
                    AddIssue(Core.Resources.CompartmentDefinitionIsDupe, entryIndex);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(compartment.Url) || !Uri.IsWellFormedUriString(compartment.Url, UriKind.Absolute))
                {
                    AddIssue(Core.Resources.CompartmentDefinitionInvalidUrl, entryIndex);
                    continue;
                }

                var resourceTypes = compartment.Resource.Where(r => r.Code.HasValue).Select(r => r.Code.Value);
                if (resourceTypes.Count() != resourceTypes.Distinct().Count())
                {
                    AddIssue(Core.Resources.CompartmentDefinitionDupeResource, entryIndex);
                    continue;
                }

                validatedCompartments.Add(compartment.Code.Value, compartment);
            }

            if (issues.Count != 0)
            {
                throw new InvalidDefinitionException(
                    Core.Resources.CompartmentDefinitionContainsInvalidEntry,
                    issues.ToArray());
            }

            return validatedCompartments;

            void AddIssue(string format, params object[] args)
            {
                issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Fatal,
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, format, args)));
            }
        }

        private static Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>> BuildResourceTypeLookup(ICollection<CompartmentDefinition> compartmentDefinitions)
        {
            var resourceTypeParamsByCompartmentDictionary = new Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>>();

            foreach (CompartmentDefinition compartment in compartmentDefinitions)
            {
                foreach (ResourceComponent resource in compartment.Resource)
                {
                    if (!resourceTypeParamsByCompartmentDictionary.TryGetValue(resource.Code.Value, out Dictionary<CompartmentType, HashSet<string>> resourceTypeDict))
                    {
                        resourceTypeDict = new Dictionary<CompartmentType, HashSet<string>>();
                        resourceTypeParamsByCompartmentDictionary.Add(resource.Code.Value, resourceTypeDict);
                    }

                    resourceTypeDict[compartment.Code.Value] = resource.Param?.ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }

            return resourceTypeParamsByCompartmentDictionary;
        }
    }
}

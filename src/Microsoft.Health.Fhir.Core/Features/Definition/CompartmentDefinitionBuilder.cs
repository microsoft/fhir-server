// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.CompartmentDefinition;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal class CompartmentDefinitionBuilder
    {
        private Lazy<(Dictionary<CompartmentType, CompartmentDefinition>, Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>>)> _compartmentResources;

        internal CompartmentDefinitionBuilder(Bundle bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));
            _compartmentResources = new Lazy<(Dictionary<CompartmentType, CompartmentDefinition>, Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>>)>(() => Build(bundle));
        }

        /// <summary>
        /// Given a fhir resource type, you can get the search params by compartment type.
        /// Example (Appointment (FhirResource), { Patient (CompartmentType), actor (searchparam) })
        /// </summary>
        internal Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>> CompartmentSearchParams
        {
            get
            {
                return _compartmentResources.Value.ToTuple().Item2;
            }
        }

        internal Dictionary<CompartmentType, CompartmentDefinition> CompartmentLookup
        {
            get
            {
                return _compartmentResources.Value.ToTuple().Item1;
            }
        }

        private (Dictionary<CompartmentType, CompartmentDefinition>, Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>>) Build(Bundle bundle)
        {
            Dictionary<CompartmentType, CompartmentDefinition> compartmentTypeDictionary = ValidateAndGetCompartmentDict(bundle);

            Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>> compartmentLookup = BuildResourceTypeLookup(compartmentTypeDictionary.Values.ToArray());

            return (compartmentTypeDictionary, compartmentLookup);
       }

        private Dictionary<CompartmentType, CompartmentDefinition> ValidateAndGetCompartmentDict(Bundle bundle)
        {
            var issues = new List<IssueComponent>();
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

                if (string.IsNullOrWhiteSpace(compartment.Url) || !Uri.IsWellFormedUriString(compartment.Url, UriKind.Absolute))
                {
                    AddIssue(Core.Resources.CompartmentDefinitionInvalidUrl, entryIndex);
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
                issues.Add(new IssueComponent()
                {
                    Severity = IssueSeverity.Fatal,
                    Code = IssueType.Invalid,
                    Diagnostics = string.Format(CultureInfo.InvariantCulture, format, args),
                });
            }
        }

        private static Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>> BuildResourceTypeLookup(CompartmentDefinition[] compartmentDefinitions)
        {
            var resourceTypeParamsByCompartmentDictionary = new Dictionary<ResourceType, Dictionary<CompartmentType, HashSet<string>>>();

            foreach (CompartmentDefinition compartment in compartmentDefinitions)
            {
                foreach (ResourceComponent resource in compartment.Resource)
                {
                    if (!resourceTypeParamsByCompartmentDictionary.TryGetValue(resource.Code.Value, out var resourceTypeDict))
                    {
                        resourceTypeDict = new Dictionary<CompartmentType, HashSet<string>>();
                        resourceTypeParamsByCompartmentDictionary.Add(resource.Code.Value, resourceTypeDict);
                    }

                    if (resourceTypeDict.ContainsKey(compartment.Code.Value))
                    {
                        // Already populated
                        continue;
                    }

                    resourceTypeDict[compartment.Code.Value] = resource.Param?.ToList().AsReadOnly().ToHashSet();
                }
            }

            return resourceTypeParamsByCompartmentDictionary;
        }
    }
}

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
        private readonly Bundle _bundle;

        private Dictionary<CompartmentType, CompartmentDefinition> _compartmentTypeDictionary = new Dictionary<CompartmentType, CompartmentDefinition>();

        // Given a fhir resource type, you can get the search params by compartment type.
        // Example (Appointment (FhirResource), { Patient (CompartmentType), actor (searchparam) })
        private Dictionary<ResourceType, Dictionary<CompartmentType, List<string>>> _resourceTypeParamsByCompartmentDictionary = new Dictionary<ResourceType, Dictionary<CompartmentType, List<string>>>();

        private bool _initialized;

        internal CompartmentDefinitionBuilder(Bundle bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));
            _bundle = bundle;
        }

        internal IDictionary<ResourceType, IReadOnlyDictionary<CompartmentType, IReadOnlyList<string>>> CompartmentSearchParams
        {
            get
            {
                if (!_initialized)
                {
                    Build();
                }

                return _resourceTypeParamsByCompartmentDictionary as IDictionary<ResourceType, IReadOnlyDictionary<CompartmentType, IReadOnlyList<string>>>;
            }
        }

        internal IDictionary<CompartmentType, CompartmentDefinition> CompartmentLookup
        {
            get
            {
                if (!_initialized)
                {
                    Build();
                }

                return _compartmentTypeDictionary;
            }
        }

        internal void Build()
        {
            _compartmentTypeDictionary = ValidateAndGetCompartmentDict();

            // Build the lookup for the compartment by resource type.
            foreach (var entry in _compartmentTypeDictionary)
            {
                BuildResourceTypeLookup(entry.Value);
            }

            _initialized = true;
        }

        private Dictionary<CompartmentType, CompartmentDefinition> ValidateAndGetCompartmentDict()
        {
            var issues = new List<IssueComponent>();
            var validatedCompartments = new Dictionary<CompartmentType, CompartmentDefinition>();

            for (int entryIndex = 0; entryIndex < _bundle.Entry.Count; entryIndex++)
            {
                // Make sure resources are not null and they are Compartment.
                EntryComponent entry = _bundle.Entry[entryIndex];

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

        private void BuildResourceTypeLookup(CompartmentDefinition compartment)
        {
            foreach (ResourceComponent resource in compartment.Resource)
            {
                if (!_resourceTypeParamsByCompartmentDictionary.TryGetValue(resource.Code.Value, out var resourceTypeDict))
                {
                    resourceTypeDict = new Dictionary<CompartmentType, List<string>>();
                    _resourceTypeParamsByCompartmentDictionary.Add(resource.Code.Value, resourceTypeDict);
                }

                if (resourceTypeDict.ContainsKey(compartment.Code.Value))
                {
                    // Already populated
                    continue;
                }

                resourceTypeDict[compartment.Code.Value] = resource.Param?.ToList();
            }

            return;
        }
    }
}

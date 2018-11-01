// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class CompartmentIndices
    {
        private readonly ICompartmentIndexer _compartmentIndexer;

        [JsonConstructor]
        public CompartmentIndices()
        {
        }

        public CompartmentIndices(ICompartmentIndexer compartmentIndexer)
        {
            EnsureArg.IsNotNull(compartmentIndexer, nameof(compartmentIndexer));
            _compartmentIndexer = compartmentIndexer;
        }

        [JsonProperty(KnownResourceWrapperProperties.Device)]
        public IReadOnlyCollection<string> DeviceCompartmentEntry { get; private set; }

        [JsonProperty(KnownResourceWrapperProperties.Encounter)]
        public IReadOnlyCollection<string> EncounterCompartmentEntry { get; private set; }

        [JsonProperty(KnownResourceWrapperProperties.Patient)]
        public IReadOnlyCollection<string> PatientCompartmentEntry { get; private set; }

        [JsonProperty(KnownResourceWrapperProperties.Practitioner)]
        public IReadOnlyCollection<string> PractitionerCompartmentEntry { get; private set; }

        [JsonProperty(KnownResourceWrapperProperties.RelatedPerson)]
        public IReadOnlyCollection<string> RelatedPersonCompartmentEntry { get; private set; }

        public void Extract(ResourceType resourceType, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            EnsureArg.IsNotNull(searchIndices, nameof(searchIndices));
            DeviceCompartmentEntry = _compartmentIndexer.Extract(resourceType, CompartmentType.Device, searchIndices);
            EncounterCompartmentEntry = _compartmentIndexer.Extract(resourceType, CompartmentType.Encounter, searchIndices);
            PatientCompartmentEntry = _compartmentIndexer.Extract(resourceType, CompartmentType.Patient, searchIndices);
            PractitionerCompartmentEntry = _compartmentIndexer.Extract(resourceType, CompartmentType.Practitioner, searchIndices);
            RelatedPersonCompartmentEntry = _compartmentIndexer.Extract(resourceType, CompartmentType.RelatedPerson, searchIndices);
        }
    }
}

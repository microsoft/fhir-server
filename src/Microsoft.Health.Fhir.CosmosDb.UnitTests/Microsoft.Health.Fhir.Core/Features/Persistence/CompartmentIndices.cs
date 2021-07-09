// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class CompartmentIndices
    {
        [JsonConstructor]
        public CompartmentIndices()
        {
        }

        internal CompartmentIndices(IDictionary<string, IReadOnlyCollection<string>> compartmentTypeToResourceIds)
        {
            EnsureArg.IsNotNull(compartmentTypeToResourceIds, nameof(compartmentTypeToResourceIds));
            IReadOnlyCollection<string> compartmentEntry = null;

            if (compartmentTypeToResourceIds.TryGetValue(KnownCompartmentTypes.Device, out compartmentEntry))
            {
                DeviceCompartmentEntry = compartmentEntry;
            }

            if (compartmentTypeToResourceIds.TryGetValue(KnownCompartmentTypes.Encounter, out compartmentEntry))
            {
                EncounterCompartmentEntry = compartmentEntry;
            }

            if (compartmentTypeToResourceIds.TryGetValue(KnownCompartmentTypes.Patient, out compartmentEntry))
            {
                PatientCompartmentEntry = compartmentEntry;
            }

            if (compartmentTypeToResourceIds.TryGetValue(KnownCompartmentTypes.Practitioner, out compartmentEntry))
            {
                PractitionerCompartmentEntry = compartmentEntry;
            }

            if (compartmentTypeToResourceIds.TryGetValue(KnownCompartmentTypes.RelatedPerson, out compartmentEntry))
            {
                RelatedPersonCompartmentEntry = compartmentEntry;
            }
        }

        [JsonProperty(KnownResourceWrapperProperties.Device)]
        public IReadOnlyCollection<string> DeviceCompartmentEntry { get; }

        [JsonProperty(KnownResourceWrapperProperties.Encounter)]
        public IReadOnlyCollection<string> EncounterCompartmentEntry { get; }

        [JsonProperty(KnownResourceWrapperProperties.Patient)]
        public IReadOnlyCollection<string> PatientCompartmentEntry { get; }

        [JsonProperty(KnownResourceWrapperProperties.Practitioner)]
        public IReadOnlyCollection<string> PractitionerCompartmentEntry { get; }

        [JsonProperty(KnownResourceWrapperProperties.RelatedPerson)]
        public IReadOnlyCollection<string> RelatedPersonCompartmentEntry { get; }
    }
}

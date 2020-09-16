// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.ValueSets
{
    public enum CompartmentType
    {
        [EnumLiteral("Patient", "http://hl7.org/fhir/compartment-type")]
        Patient,

        [EnumLiteral("Encounter", "http://hl7.org/fhir/compartment-type")]
        Encounter,

        [EnumLiteral("RelatedPerson", "http://hl7.org/fhir/compartment-type")]
        RelatedPerson,

        [EnumLiteral("Practitioner", "http://hl7.org/fhir/compartment-type")]
        Practitioner,

        [EnumLiteral("Device", "http://hl7.org/fhir/compartment-type")]
        Device,
    }
}

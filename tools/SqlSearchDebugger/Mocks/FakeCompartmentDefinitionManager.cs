// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.ValueSets;

namespace SqlSearchDebugger.Mocks;

/// <summary>
/// Fake compartment definition manager for debugging. Provides basic Patient compartment definitions.
/// </summary>
public class FakeCompartmentDefinitionManager : ICompartmentDefinitionManager
{
    private static readonly Dictionary<CompartmentType, HashSet<string>> _compartmentResourceTypes = new()
    {
        {
            CompartmentType.Patient, new HashSet<string>
            {
                "Observation", "Encounter", "Condition", "Procedure", "MedicationRequest",
                "DiagnosticReport", "AllergyIntolerance", "CarePlan", "Immunization",
                "DocumentReference", "Claim", "Coverage", "Device",
            }
        },
        {
            CompartmentType.Practitioner, new HashSet<string>
            {
                "Observation", "Encounter", "Procedure", "DiagnosticReport", "Appointment",
            }
        },
    };

    private static readonly Dictionary<(string ResourceType, CompartmentType), HashSet<string>> _compartmentSearchParams = new()
    {
        { ("Observation", CompartmentType.Patient), new HashSet<string> { "subject", "performer" } },
        { ("Encounter", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("Condition", CompartmentType.Patient), new HashSet<string> { "patient", "asserter" } },
        { ("Procedure", CompartmentType.Patient), new HashSet<string> { "patient", "performer" } },
        { ("MedicationRequest", CompartmentType.Patient), new HashSet<string> { "subject" } },
        { ("DiagnosticReport", CompartmentType.Patient), new HashSet<string> { "subject" } },
        { ("AllergyIntolerance", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("CarePlan", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("Immunization", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("DocumentReference", CompartmentType.Patient), new HashSet<string> { "subject" } },
        { ("Claim", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("Coverage", CompartmentType.Patient), new HashSet<string> { "patient", "beneficiary" } },
        { ("Device", CompartmentType.Patient), new HashSet<string> { "patient" } },
        { ("Observation", CompartmentType.Practitioner), new HashSet<string> { "performer" } },
        { ("Encounter", CompartmentType.Practitioner), new HashSet<string> { "practitioner" } },
        { ("Procedure", CompartmentType.Practitioner), new HashSet<string> { "performer" } },
        { ("DiagnosticReport", CompartmentType.Practitioner), new HashSet<string> { "performer" } },
        { ("Appointment", CompartmentType.Practitioner), new HashSet<string> { "actor" } },
    };

    public bool TryGetResourceTypes(CompartmentType compartmentType, out HashSet<string> resourceTypes)
    {
        return _compartmentResourceTypes.TryGetValue(compartmentType, out resourceTypes);
    }

    public bool TryGetSearchParams(string resourceType, CompartmentType compartmentType, out HashSet<string> searchParams)
    {
        return _compartmentSearchParams.TryGetValue((resourceType, compartmentType), out searchParams);
    }
}

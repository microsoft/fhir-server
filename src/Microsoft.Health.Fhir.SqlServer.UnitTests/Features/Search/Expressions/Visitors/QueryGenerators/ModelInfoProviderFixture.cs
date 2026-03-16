// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Fixture to initialize the ModelInfoProvider for CompartmentQueryGeneratorTests.
    /// </summary>
    public class ModelInfoProviderFixture
    {
        public ModelInfoProviderFixture()
        {
            var provider = MockModelInfoProviderBuilder
                .Create(FhirSpecification.R4)
                .AddKnownTypes("Encounter", "Device", "Practitioner", "RelatedPerson", "Claim", "Appointment", "Condition")
                .Build();

            // Manually override the compartment types to include all standard FHIR compartments
            provider.GetCompartmentTypeNames().Returns(new[] { "Patient", "Practitioner", "Encounter", "Device", "RelatedPerson" });
            provider.IsKnownCompartmentType(Arg.Any<string>()).Returns(x => new[] { "Patient", "Practitioner", "Encounter", "Device", "RelatedPerson" }.Contains((string)x[0]));

            ModelInfoProvider.SetProvider(provider);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    public class CompartmentDefinitionManagerTests
    {
        private CompartmentDefinitionManager _validBuiltCompartment;

        public CompartmentDefinitionManagerTests()
        {
            var validCompartmentBundle = Samples.GetJsonSample<Bundle>("ValidCompartmentDefinition");
            _validBuiltCompartment = new CompartmentDefinitionManager(new FhirJsonParser());
            _validBuiltCompartment.Build(validCompartmentBundle);
        }

        [Theory]
        [InlineData(ResourceType.Condition, CompartmentType.Patient, 2)]
        [InlineData(ResourceType.Condition, CompartmentType.Encounter, 1)]
        [InlineData(ResourceType.Encounter, CompartmentType.Patient, 1)]
        [InlineData(ResourceType.Observation, CompartmentType.Encounter, 1)]
        public void GivenAValidCompartmentDefinitionBundle_WhenValidated_ThenValidSearchParams(ResourceType resourceType, CompartmentType compartmentType, int testCount)
        {
            Assert.True(_validBuiltCompartment.TryGetSearchParams(resourceType.ToString(), compartmentType.ToString(), out HashSet<string> searchParams));
            Assert.Equal(testCount, searchParams.Count);
        }

        [Fact]
        public void GivenAnInvalidCompartmentDefinitionBundle_Issues_MustBeReturned()
        {
            var invalidCompartmentBundle = Samples.GetJsonSample<Bundle>("InvalidCompartmentDefinition");
            var invalidBuiltCompartment = new CompartmentDefinitionManager(new FhirJsonParser());
            var exception = Assert.Throws<InvalidDefinitionException>(() => invalidBuiltCompartment.Build(invalidCompartmentBundle));
            Assert.Contains("invalid entries", exception.Message);
            Assert.Equal(3, exception.Issues.Count);
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("not a CompartmentDefinition"));
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("url is invalid"));
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("bundle.entry[1].resource has duplicate resources."));
        }
    }
}

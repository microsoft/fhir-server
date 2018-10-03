// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    public class CompartmentDefinitionBuilderTests
    {
        private CompartmentDefinitionBuilder _validBuiltCompartment;

        public CompartmentDefinitionBuilderTests()
        {
            var validCompartmentBundle = Samples.GetJsonSample<Bundle>("ValidCompartmentDefinition");
            _validBuiltCompartment = new CompartmentDefinitionBuilder(validCompartmentBundle);
            _validBuiltCompartment.Build();
        }

        [Fact]
        public void GivenAValidCompartDefinitionBundle_NoIssues_And_ValidCounts()
        {
            Assert.Equal(5, _validBuiltCompartment.CompartmentSearchParams.Count);
            Assert.Equal(2, _validBuiltCompartment.CompartmentLookup.Count);
        }

        [Fact]
        public void GivenAValidCompartDefinitionBundle_NoIssues_And_ValidSearchParams()
        {
            var conditionDict = _validBuiltCompartment.CompartmentSearchParams[ResourceType.Condition];
            Assert.Equal(2, conditionDict.Count);
            Assert.True(conditionDict.ContainsKey(CompartmentType.Patient));
            Assert.True(conditionDict.ContainsKey(CompartmentType.Encounter));
            var communicationDict = _validBuiltCompartment.CompartmentSearchParams[ResourceType.Communication];
            Assert.Equal(1, communicationDict.Count);
            Assert.True(communicationDict.ContainsKey(CompartmentType.Patient));
        }

        [Fact]
        public void GivenAnInvalidCompartmentDefinitionBundle_Issues_MustBeReturned()
        {
            var invalidCompartmentBundle = Samples.GetJsonSample<Bundle>("InvalidCompartmentDefinition");
            var invalidBuiltCompartment = new CompartmentDefinitionBuilder(invalidCompartmentBundle);
            var exception = Assert.Throws<InvalidDefinitionException>(() => invalidBuiltCompartment.Build());
            Assert.Contains("invalid entries", exception.Message);
            Assert.Equal(2, exception.Issues.Count);
            Assert.NotNull(exception.Issues.SingleOrDefault(ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal && ic.Code == OperationOutcome.IssueType.Invalid && ic.Diagnostics.Contains("not a CompartmentDefinition")));
            Assert.NotNull(exception.Issues.SingleOrDefault(ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal && ic.Code == OperationOutcome.IssueType.Invalid && ic.Diagnostics.Contains("url is invalid")));
        }
    }
}

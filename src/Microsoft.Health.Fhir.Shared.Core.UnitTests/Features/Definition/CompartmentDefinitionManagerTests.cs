// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class CompartmentDefinitionManagerTests
    {
        private CompartmentDefinitionManager _validBuiltCompartment;

        public CompartmentDefinitionManagerTests()
        {
            var validCompartmentBundle = Samples.GetJsonSample<Bundle>("ValidCompartmentDefinition");
            _validBuiltCompartment = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            _validBuiltCompartment.Build(new BundleWrapper(validCompartmentBundle.ToTypedElement()));
        }

        [Theory]
        [InlineData(ResourceType.Condition, CompartmentType.Patient, 2)]
        [InlineData(ResourceType.Condition, CompartmentType.Encounter, 1)]
        [InlineData(ResourceType.Encounter, CompartmentType.Patient, 1)]
        [InlineData(ResourceType.Observation, CompartmentType.Encounter, 1)]
        public void GivenAValidCompartmentDefinitionBundle_WhenValidated_ThenValidSearchParams(ResourceType resourceType, CompartmentType compartmentType, int testCount)
        {
            Assert.True(_validBuiltCompartment.TryGetSearchParams(resourceType.ToString(), compartmentType, out HashSet<string> searchParams));
            Assert.Equal(testCount, searchParams.Count);
        }

        [Theory]
        [InlineData(CompartmentType.Encounter)]
        [InlineData(CompartmentType.Patient)]
        public void GivenAValidCompartmentDefinitionBundle_WhenValidated_ThenValidResourceTypes(CompartmentType compartmentType)
        {
            Assert.True(_validBuiltCompartment.TryGetResourceTypes(compartmentType, out HashSet<string> resourceTypes));
            Assert.True(resourceTypes.Count > 0);
        }

        [Fact]
        public void GivenAnInvalidCompartmentDefinitionBundle_Issues_MustBeReturned()
        {
            var invalidCompartmentBundle = Samples.GetJsonSample<Bundle>("InvalidCompartmentDefinition");
            var invalidBuiltCompartment = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            var exception = Assert.Throws<InvalidDefinitionException>(() => invalidBuiltCompartment.Build(new BundleWrapper(invalidCompartmentBundle.ToTypedElement())));
            Assert.Contains("invalid entries", exception.Message);
            Assert.Equal(3, exception.Issues.Count);
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("not a CompartmentDefinition"));
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("url is invalid"));
            Assert.Contains(exception.Issues, ic => ic.Severity == OperationOutcome.IssueSeverity.Fatal.ToString() && ic.Code == OperationOutcome.IssueType.Invalid.ToString() && ic.Diagnostics.Contains("bundle.entry[1].resource has duplicate resources."));
        }
    }
}

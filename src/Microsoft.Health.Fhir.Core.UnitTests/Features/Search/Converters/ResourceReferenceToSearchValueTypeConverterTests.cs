// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class ResourceReferenceToSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<ResourceReferenceToSearchValueTypeConverter, ResourceReference>
    {
        [Fact]
        public void GivenAResourceReferenceWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(s => s.Reference = null);
        }

        [Fact]
        public void GivenAResourceReferenceWithContainedReference_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(s => s.Reference = "#patient");
        }

        [Fact]
        public void GivenAResourceReferenceWithReference_WhenConverted_ThenAReferenceSearchValueShouldBeCreated()
        {
            const string reference = "Patient/123";

            Test(
                r => r.Reference = reference,
                ValidateReference,
                reference);
        }
    }
}

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
    public class FhirDecimalToDecimalSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<FhirDecimalToNumberSearchValueTypeConverter, FhirDecimal>
    {
        [Fact]
        public void GivenAFhirDecimalWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(d => d.Value = null);
        }

        [Fact]
        public void GivenAFhirDecimalWithValue_WhenConverted_ThenASearchValueShouldBeCreated()
        {
            const decimal value = 190.5m;

            Test(
                d => d.Value = value,
                ValidateNumber,
                value);
        }
    }
}

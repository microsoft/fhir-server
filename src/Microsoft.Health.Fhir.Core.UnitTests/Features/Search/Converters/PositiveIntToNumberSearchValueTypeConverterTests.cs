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
    public class PositiveIntToNumberSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<PositiveIntToNumberSearchValueTypeConverter, PositiveInt>
    {
        [Fact]
        public void GivenAPositiveIntWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(pi => pi.Value = null);
        }

        [Fact]
        public void GivenAPositiveIntWithValue_WhenConverted_ThenANumberValueShouldBeCreated()
        {
            const int value = 10;

            Test(
                pi => pi.Value = value,
                ValidateNumber,
                (decimal)value);
        }
    }
}

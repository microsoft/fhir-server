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
    public class IntegerToIntegerSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<IntegerToNumberSearchValueTypeConverter, Integer>
    {
        [Fact]
        public void GivenAIntegerWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(i => i.Value = null);
        }

        [Fact]
        public void GivenAIntegerWithValue_WhenConverted_ThenANumberValueShouldBeCreated()
        {
            const int value = 500;

            Test(
                i => i.Value = value,
                ValidateNumber,
                (decimal)value);
        }
    }
}

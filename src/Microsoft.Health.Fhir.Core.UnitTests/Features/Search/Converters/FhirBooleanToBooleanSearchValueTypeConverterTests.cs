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
    public class FhirBooleanToBooleanSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<FhirBooleanToTokenSearchValueTypeConverter, FhirBoolean>
    {
        [Fact]
        public void GivenAFhirBooleanWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(b => b.Value = null);
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public void GivenAFhirBooleanWithValue_WhenConverted_ThenATokenSearchValueShouldBeCreated(bool value, string expected)
        {
            Test(
                b => b.Value = value,
                ValidateToken,
                new Token("http://hl7.org/fhir/special-values", expected));
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class BooleanToBooleanSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<BooleanToTokenSearchValueConverter, FhirBoolean>
    {
        [Fact]
        public async Task GivenAFhirBooleanWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(b => b.Value = null);
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public async Task GivenAFhirBooleanWithValue_WhenConverted_ThenATokenSearchValueShouldBeCreated(bool value, string expected)
        {
            await Test(
                b => b.Value = value,
                ValidateToken,
                new Token("http://hl7.org/fhir/special-values", expected));
        }
    }
}

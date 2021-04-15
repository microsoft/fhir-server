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
    public class DecimalToDecimalSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<DecimalToNumberSearchValueConverter, FhirDecimal>
    {
        [Fact]
        public async Task GivenAFhirDecimalWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(d => d.Value = null);
        }

        [Fact]
        public async Task GivenAFhirDecimalWithValue_WhenConverted_ThenASearchValueShouldBeCreated()
        {
            const decimal value = 190.5m;

            await Test(
                d => d.Value = value,
                ValidateNumber,
                value);
        }
    }
}

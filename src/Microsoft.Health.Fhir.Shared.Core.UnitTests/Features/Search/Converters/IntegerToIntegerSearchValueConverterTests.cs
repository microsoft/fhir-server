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
    public class IntegerToIntegerSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<IntegerToNumberSearchValueConverter, Integer>
    {
        [Fact]
        public async Task GivenAIntegerWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(i => i.Value = null);
        }

        [Fact]
        public async Task GivenAIntegerWithValue_WhenConverted_ThenANumberValueShouldBeCreated()
        {
            const int value = 500;

            await Test(
                i => i.Value = value,
                ValidateNumber,
                (decimal)value);
        }
    }
}

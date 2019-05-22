// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class RangeToQuantitySearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<RangeToQuantitySearchValueTypeConverter, Range>
    {
        [InlineData(10, 20)]
        [InlineData(null, 10)]
        [InlineData(10, null)]
        [Theory]
        public void GivenRange_WhenConvertedToANumber_ThenANumberWithCorrectLowAndHighValuesIsCreated(int? low, int? high)
        {
            const string system = "s";
            const string code = "g";

            Test(
                r =>
                {
                    if (low.HasValue)
                    {
                        r.Low = new Quantity(low.Value, code, system);
                    }

                    if (high.HasValue)
                    {
                        r.High = new Quantity(high.Value, code, system);
                    }
                },
                (expected, sv) => Assert.Equal(expected.ToString(), sv.ToString()),
                new QuantitySearchValue(system, code, low, high));
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class RangeToNumberSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<RangeToNumberSearchValueConverter, Range>
    {
        [Fact]
        public async Task GivenARangeWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(
                r =>
                {
                    r.Low = null;
                    r.High = null;
                });
        }

        [Fact]
        public async Task GivenARangeWithValue_WhenConverted_ThenANumberValueShouldBeCreated()
        {
            const decimal lowValue = 100.123456m;
            const decimal highValue = 200.123456m;

            var lowQuantity = new Quantity(lowValue, null, null);
            var highQuantity = new Quantity(highValue, null, null);
            await Test(
                r =>
                {
                    r.Low = lowQuantity;
                    r.High = highQuantity;
                },
                ValidateNumberRange,
                new Range
                {
                    Low = lowQuantity,
                    High = highQuantity,
                });
        }

        [Fact]
        public async Task GivenARangeWithOnlyLowValue_WhenConverted_ThenANumberWithLowValueShouldBeCreated()
        {
            const decimal lowValue = 100.123456m;
            var lowQuanity = new Quantity(lowValue, null, null);

            await Test(
                r =>
                {
                    r.Low = lowQuanity;
                    r.High = null;
                },
                ValidateNumberRange,
                new Range
                {
                    Low = lowQuanity,
                    High = null,
                });
        }
    }
}

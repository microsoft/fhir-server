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
    public class DateToDateTimeSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<DateToDateTimeSearchValueConverter, Date>
    {
        [Fact]
        public async Task GivenADateWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(date => date.Value = null);
        }

        [Fact]
        public async Task GivenADateWithValue_WhenConverted_ThenADateTimeSearchValueShouldBeCreated()
        {
            const string partialDate = "2018-01-05";

            await Test(
                date => date.Value = partialDate,
                ValidateDateTime,
                partialDate);
        }
    }
}

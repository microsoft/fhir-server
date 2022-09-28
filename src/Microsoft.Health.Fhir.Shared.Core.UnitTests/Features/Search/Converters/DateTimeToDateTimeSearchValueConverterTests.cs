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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class DateTimeToDateTimeSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<DateToDateTimeSearchValueConverter, FhirDateTime>
    {
        [Fact]
        public async Task GivenAFhirDateTimeWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(date => date.Value = null);
        }

        [Fact]
        public async Task GivenAFhirDateTimeWithValue_WhenConverted_ThenADateTimeSearchValueShouldBeCreated()
        {
            const string partialDate = "2018-03-30T05:12";

            await Test(
                date => date.Value = partialDate,
                ValidateDateTime,
                partialDate);
        }
    }
}

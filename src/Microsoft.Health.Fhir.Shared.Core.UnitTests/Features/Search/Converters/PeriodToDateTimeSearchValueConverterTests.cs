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
    public class PeriodToDateTimeSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<PeriodToDateTimeSearchValueConverter, Period>
    {
        [Theory]
        [InlineData("2018-01", "2018-12-25T15:30")]
        [InlineData(null, "2017")]
        [InlineData("2018-10-15T12:33:55", null)]
        public async Task GivenAPeriodWithValue_WhenConverted_ThenADateTimeSearchValueShouldBeCreated(string start, string end)
        {
            string expectedStart = start ?? "0001-01-01T00:00:00";
            string expectedEnd = end ?? "9999-12-31T23:59:59";

            await Test(
                period =>
                {
                    period.Start = start;
                    period.End = end;
                },
                ValidateDateTime,
                (expectedStart, expectedEnd));
        }
    }
}

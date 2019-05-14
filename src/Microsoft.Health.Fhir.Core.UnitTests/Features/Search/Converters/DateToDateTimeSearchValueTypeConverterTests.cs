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
    public class DateToDateTimeSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<DateToDateTimeSearchValueTypeConverter, Date>
    {
        [Fact]
        public void GivenADateWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(date => date.Value = null);
        }

        [Fact]
        public void GivenADateWithValue_WhenConverted_ThenADateTimeSearchValueShouldBeCreated()
        {
            const string partialDate = "2018-01-05";

            Test(
                date => date.Value = partialDate,
                ValidateDateTime,
                partialDate);
        }
    }
}

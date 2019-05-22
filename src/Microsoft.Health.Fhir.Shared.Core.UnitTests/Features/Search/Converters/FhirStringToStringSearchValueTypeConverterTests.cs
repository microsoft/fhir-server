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
    public class FhirStringToStringSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<FhirStringToStringSearchValueTypeConverter, FhirString>
    {
        [Fact]
        public void GivenAFhirStringWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(s => s.Value = null);
        }

        [Fact]
        public void GivenAFhirStringWithValue_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string value = "test";

            Test(
                s => s.Value = value,
                ValidateString,
                value);
        }
    }
}

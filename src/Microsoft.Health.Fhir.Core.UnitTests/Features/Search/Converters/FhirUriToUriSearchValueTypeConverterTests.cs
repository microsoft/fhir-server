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
    public class FhirUriToUriSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<FhirUriToUriSearchValueTypeConverter, FhirUri>
    {
        [Fact]
        public void GivenAFhirUriWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(uri => uri.Value = null);
        }

        [Fact]
        public void GivenAFhirUriWithValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri";

            Test(
                uri => uri.Value = value,
                ValidateUri,
                value);
        }
    }
}

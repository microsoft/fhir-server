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
    public class UriToUriSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<UriToUriSearchValueConverter, FhirUri>
    {
        [Fact]
        public async Task GivenAFhirUriWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(uri => uri.Value = null);
        }

        [Fact]
        public async Task GivenAFhirUriWithValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri";

            await Test(
                uri => uri.Value = value,
                ValidateUri,
                value);
        }
    }
}

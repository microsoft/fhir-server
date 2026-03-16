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
    public class ReferenceToUriSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<ReferenceToUriSearchValueConverter, ResourceReference>
    {
        [Fact]
        public async Task GivenAFhirReferenceWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(uri => uri.Reference = null);
        }

        [Fact]
        public async Task GivenAFhirReferenceWithBasicValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri";

            await Test(
                uri => uri.Reference = value,
                ValidateUri,
                value);
        }

        [Fact]
        public async Task GivenAFhirReferenceWithReferenceValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "Patient/123ABC";

            await Test(
                uri => uri.Reference = value,
                ValidateUri,
                value);
        }

        [Fact]
        public async Task GivenAFhirReferenceWithInvalidURL_WhenConverted_ThenSearchValueShouldBeCreated()
        {
            const string value = "this is not a valid url";

            await Test(
                uri => uri.Reference = value,
                ValidateUri,
                value);
        }
    }
}

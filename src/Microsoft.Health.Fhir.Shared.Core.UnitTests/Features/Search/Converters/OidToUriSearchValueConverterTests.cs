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
    public class OidToUriSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<OidToUriSearchValueConverter, Oid>
    {
        [Fact]
        public async Task GivenAOidWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(oid => oid.Value = null);
        }

        [Fact]
        public async Task GivenAOidWithValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string id = "1.3.6.1.4.1.343";

            await Test(
                oid => oid.Value = id,
                ValidateUri,
                id);
        }
    }
}

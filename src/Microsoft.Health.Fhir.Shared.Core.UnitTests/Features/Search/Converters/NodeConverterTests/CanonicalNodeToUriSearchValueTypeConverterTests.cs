// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task=System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    #if !Stu3
    public class CanonicalNodeToUriSearchValueTypeConverterTests : FhirNodeToSearchValueTypeConverterTests<CanonicalNodeToUriSearchValueTypeConverter, Canonical>
    {
        [Fact]
        public async Task GivenACanonicalWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(uri => uri.Value = null);
        }

        [Fact]
        public async Task GivenACanonicalWithUriValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri";

            await Test(
                uri => uri.Value = value,
                ValidateUri,
                value);
        }

        [Fact]
        public async Task GivenACanonicalWithUriAndVersionValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri|1.0.0";

            await Test(
                uri => uri.Value = value,
                ValidateCanonical,
                value);
        }

        [Fact]
        public async Task GivenACanonicalWithReferenceAndVersionValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "ValueSet/1|2";

            await Test(
                uri => uri.Value = value,
                ValidateCanonical,
                value);
        }

        [Fact]
        public async Task GivenACanonicalWithUriAndVersionAndFragmentValue_WhenConverted_ThenAUriSearchValueShouldBeCreated()
        {
            const string value = "http://uri|1.0.0#name1";

            await Test(
                uri => uri.Value = value,
                ValidateCanonical,
                value);
        }
    }
    #endif
}

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
    public class IdentifierToTokenSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<IdentifierToTokenSearchValueConverter, Identifier>
    {
        [Fact]
        public async Task GivenAnIdentifierWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(id => id.Value = null);
        }

        [Theory]
        [InlineData("system", "identifier", "text")]
        [InlineData(null, "identifier", "text")]
        [InlineData("system", "identifier", null)]
        public async Task GivenAnIdentifierWithValue_WhenConverted_ThenATokenSearchValueShouldBeCreated(string system, string identifier, string text)
        {
            await Test(
                id =>
                {
                    id.System = system;
                    id.Value = identifier;

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        id.Type = new CodeableConcept(null, null, text);
                    }
                },
                ValidateToken,
                new Token(system, identifier, text));
        }
    }
}

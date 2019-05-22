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
    public class IdentifierToTokenSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<IdentifierToTokenSearchValueTypeConverter, Identifier>
    {
        [Fact]
        public void GivenAnIdentifierWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(id => id.Value = null);
        }

        [Theory]
        [InlineData("system", "identifier", "text")]
        [InlineData(null, "identifier", "text")]
        [InlineData("system", "identifier", null)]
        public void GivenAnIdentifierWithValue_WhenConverted_ThenATokenSearchValueShouldBeCreated(string system, string identifier, string text)
        {
            Test(
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

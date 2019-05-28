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
    public class CodingToTokenSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<CodingToTokenSearchValueTypeConverter, Coding>
    {
        [Fact]
        public void GivenAnEmptyCoding_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(
                cc =>
                {
                    cc.System = null;
                    cc.Code = null;
                    cc.Display = null;
                });
        }

        [Theory]
        [InlineData("system", null, null)]
        [InlineData(null, "code", null)]
        [InlineData(null, null, "display")]
        public void GivenAPartialCoding_WhenConverted_ThenATokenSearchValueShouldBeCreated(string system, string code, string display)
        {
            Test(
                cc =>
                {
                    cc.System = system;
                    cc.Code = code;
                    cc.Display = display;
                },
                ValidateToken,
                new Token(system, code, display));
        }
    }
}

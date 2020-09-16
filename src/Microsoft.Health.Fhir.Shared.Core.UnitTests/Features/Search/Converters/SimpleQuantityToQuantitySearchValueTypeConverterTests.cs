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
    public class SimpleQuantityToQuantitySearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<SimpleQuantityToQuantitySearchValueTypeConverter, SimpleQuantity>
    {
        [Fact]
        public void GivenASimpleQuantityWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(sq => sq.Value = null);
        }

        [Fact]
        public void GivenASimpleQuantityWithValue_WhenConverted_ThenASimpleQuantityValueShouldBeCreated()
        {
            const string system = "s";
            const string code = "g";
            const decimal value = 0.123m;

            Test(
                sq =>
                {
                    sq.System = system;
                    sq.Code = code;
                    sq.Value = value;
                },
                ValidateQuantity,
                new Quantity(value, code, system));
        }
    }
}

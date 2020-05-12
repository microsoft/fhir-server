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
    public class QuantityNodeToQuantitySearchValueTypeConverterTests : FhirNodeToSearchValueTypeConverterTests<QuantityNodeToQuantitySearchValueTypeConverter, Quantity>
    {
        [Fact]
        public void GivenAQuantityWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(q => q.Value = null);
        }

        [Fact]
        public void GivenAQuantityWithValue_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const string system = "qs";
            const string code = "qc";
            const decimal value = 100.123456m;

            Test(
                q =>
                {
                    q.System = system;
                    q.Code = code;
                    q.Value = value;
                },
                ValidateQuantity,
                new Quantity(value, code, system));
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

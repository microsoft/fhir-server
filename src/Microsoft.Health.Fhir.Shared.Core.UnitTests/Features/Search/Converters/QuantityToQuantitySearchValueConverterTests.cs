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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QuantityToQuantitySearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<QuantityToQuantitySearchValueConverter, Quantity>
    {
        [Fact]
        public async Task GivenAQuantityWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(q => q.Value = null);
        }

        [Fact]
        public async Task GivenAQuantityWithValue_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const string system = "qs";
            const string code = "qc";
            const decimal value = 100.123456m;

            await Test(
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
        public async Task GivenASimpleQuantityWithValue_WhenConverted_ThenASimpleQuantityValueShouldBeCreated()
        {
            const string system = "s";
            const string code = "g";
            const decimal value = 0.123m;

            await Test(
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

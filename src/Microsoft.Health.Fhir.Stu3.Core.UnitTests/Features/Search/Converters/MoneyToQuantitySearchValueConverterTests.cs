// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Stu3.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class MoneyToQuantitySearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<MoneyToQuantitySearchValueConverter, Money>
    {
        [Fact]
        public async Task GivenMoneyWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(m => m.Value = null);
        }

        [Fact]
        public async Task GivenMoneyWithValueCodeAndSystem_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const decimal value = 480;
            const string code = "USD";

            await Test(
                m =>
                {
                    m.Value = value;
                    m.Code = code;
                    m.System = CurrencyValueSet.CodeSystemUri;
                },
                ValidateQuantity,
                new Quantity(value, code, CurrencyValueSet.CodeSystemUri));
        }

        [Fact]
        public async Task GivenMoneyWithNoCode_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            const decimal value = 0.125m;

            await Test(m =>
            {
                m.Value = value;
                m.Code = null;
                m.System = CurrencyValueSet.CodeSystemUri;
            });
        }

        [Fact]
        public async Task GivenMoneyWithNoSystem_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const decimal value = 0.125m;
            const string code = "USD";

            await Test(
                m =>
                {
                    m.Value = value;
                    m.Code = code;
                    m.System = null;
                },
                ValidateQuantity,
                new Quantity(value, code, null));
        }
    }
}

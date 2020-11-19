// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters;
using Microsoft.Health.Fhir.ValueSets;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.R4.Core.UnitTests.Features.Search.Converters.NodeConverterTests
{
    public class MoneyNodeToQuantitySearchValueTypeConverterTests : FhirNodeToSearchValueTypeConverterTests<MoneyNodeToQuantitySearchValueTypeConverter, Money>
    {
        [Fact]
        public async Task GivenMoneyWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(m => m.Value = null);
        }

        [Fact]
        public async Task GivenMoneyWithValueAndCurrency_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const decimal value = 480;

            const string expectedSystem = CurrencyValues.System;
            string expectedUnit = Money.Currencies.USD.ToString();
            decimal expectedValue = value;

            await Test(
                m =>
                {
                    m.Value = value;
                    m.Currency = Money.Currencies.USD;
                },
                ValidateQuantity,
                new Quantity(expectedValue, expectedUnit, expectedSystem));
        }

        [Fact]
        public async Task GivenMoneyWithNoCurrency_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            const decimal value = 0.125m;

            await Test(m =>
            {
                m.Value = value;
                m.Currency = null;
            });
        }
    }
}

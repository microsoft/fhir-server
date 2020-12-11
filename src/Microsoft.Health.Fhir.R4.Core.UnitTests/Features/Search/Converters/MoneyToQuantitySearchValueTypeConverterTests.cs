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

namespace Microsoft.Health.Fhir.R4.Core.UnitTests.Features.Search.Converters
{
    public class MoneyToQuantitySearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<MoneyToQuantitySearchValueTypeConverter, Money>
    {
        [Fact]
        public void GivenMoneyWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(m => m.Value = null);
        }

        [Fact]
        public void GivenMoneyWithValueAndCurrency_WhenConverted_ThenAQuantityValueShouldBeCreated()
        {
            const decimal value = 480;
            const Money.Currencies currency = Money.Currencies.USD;

            Test(
                m =>
                {
                    m.Value = value;
                    m.Currency = currency;
                },
                ValidateQuantity,
                new Quantity(value, currency.ToString(), CurrencyValueSet.CodeSystemUri));
        }

        [Fact]
        public void GivenMoneyWithNoCurrency_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            const decimal value = 0.125m;

            Test(m =>
            {
                m.Value = value;
                m.Currency = null;
            });
        }
    }
}

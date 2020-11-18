// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Money"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class MoneyNodeToQuantitySearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<QuantitySearchValue>
    {
        public MoneyNodeToQuantitySearchValueTypeConverter()
            : base("Money")
        {
        }

        // TODO: What behaviour should be expected for Stu3?
        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var decimalValue = (decimal?)value.Scalar("value");
            var currency = value.Scalar("currency")?.ToString();

            if (decimalValue == null || currency == null)
            {
                yield break;
            }

            yield return new QuantitySearchValue(
                CurrencyValues.System,
                currency,
                decimalValue.GetValueOrDefault());
        }
    }
}

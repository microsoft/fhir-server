// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
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

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var decimalValue = (decimal?)value.Scalar("value");

            if (decimalValue == null)
            {
                yield break;
            }

            var currency = value.Scalar("currency")?.ToString();

            // Currency information is specified differently if we are running STU3.
            if (currency == null)
            {
                var code = value.Scalar("code")?.ToString();
                var system = value.Scalar("system")?.ToString();

                if (code == null)
                {
                    yield break;
                }

                yield return new QuantitySearchValue(
                    system,
                    code,
                    decimalValue.GetValueOrDefault());
            }
            else
            {
                yield return new QuantitySearchValue(
                    CurrencyValues.System,
                    currency,
                    decimalValue.GetValueOrDefault());
            }
        }
    }
}

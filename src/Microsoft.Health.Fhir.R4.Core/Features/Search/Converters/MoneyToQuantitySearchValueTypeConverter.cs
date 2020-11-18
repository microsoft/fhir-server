// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Money"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class MoneyToQuantitySearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Money, QuantitySearchValue>
    {
        protected override IEnumerable<QuantitySearchValue> ConvertTo(Money value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            Code<Money.Currencies> code = value.CurrencyElement;
            var systemAndCode = (ISystemAndCode)code;

            if (systemAndCode == null || string.IsNullOrEmpty(systemAndCode.System) || string.IsNullOrEmpty(systemAndCode.Code))
            {
                yield return null;
            }
            else
            {
                yield return new QuantitySearchValue(systemAndCode.System, systemAndCode.Code, value.Value.Value);
            }
        }
    }
}

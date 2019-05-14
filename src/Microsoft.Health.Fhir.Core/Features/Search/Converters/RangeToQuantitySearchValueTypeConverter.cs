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
    /// A converter used to convert from <see cref="Range"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class RangeToQuantitySearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Range, QuantitySearchValue>
    {
        protected override IEnumerable<QuantitySearchValue> ConvertTo(Range value)
        {
            decimal? lowValue = value.Low?.Value;
            decimal? highValue = value.High?.Value;

            if (lowValue != null || highValue != null)
            {
                // FROM https://www.hl7.org/fhir/datatypes.html#Range: "The unit and code/system elements of the low or high elements SHALL match."

                Quantity quantityRepresentative = value.Low ?? value.High;

                yield return new QuantitySearchValue(quantityRepresentative.System, quantityRepresentative.Code, lowValue, highValue);
            }
        }
    }
}

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
    /// A converter used to convert from <see cref="Range"/> to a list of <see cref="NumberSearchValue"/>.
    /// </summary>
    public class RangeToNumberSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Range, NumberSearchValue>
    {
        protected override IEnumerable<NumberSearchValue> ConvertTo(Range value)
        {
            decimal? lowValue = value.Low?.Value;
            decimal? highValue = value.High?.Value;

            if (lowValue != null || highValue != null)
            {
                yield return new NumberSearchValue(lowValue, highValue);
            }
        }
    }
}

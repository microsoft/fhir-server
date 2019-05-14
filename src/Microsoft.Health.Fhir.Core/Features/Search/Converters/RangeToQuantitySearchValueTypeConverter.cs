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
            Quantity quantityRepresentative = value.Low ?? value.High;

            yield return new QuantitySearchValue(quantityRepresentative.System, quantityRepresentative.Code, value.Low?.Value, value.High?.Value);
        }
    }
}

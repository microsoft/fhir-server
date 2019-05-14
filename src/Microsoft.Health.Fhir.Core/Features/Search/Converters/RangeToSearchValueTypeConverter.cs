// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class RangeToSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Range>
    {
        protected override IEnumerable<ISearchValue> ConvertTo(Range value, SearchParamType searchParameterType)
        {
            switch (searchParameterType)
            {
                case SearchParamType.Quantity:
                {
                    // system and codes from high and low must match
                    Quantity quantityRepresentative = value.Low ?? value.High;

                    yield return new QuantitySearchValue(quantityRepresentative.System, quantityRepresentative.Code, value.Low?.Value, value.High?.Value);
                    yield break;
                }

                case SearchParamType.Number:
                    yield return new NumberSearchValue(value.Low?.Value, value.High?.Value);
                    yield break;
            }

            throw new ArgumentOutOfRangeException(nameof(searchParameterType));
        }
    }
}

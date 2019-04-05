// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class PeriodToSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Period>
    {
        protected override IEnumerable<ISearchValue> ConvertTo(Period value)
        {
            PartialDateTime start = string.IsNullOrWhiteSpace(value.Start) ?
                PartialDateTime.MinValue :
                PartialDateTime.Parse(value.Start);

            PartialDateTime end = string.IsNullOrWhiteSpace(value.End) ?
                PartialDateTime.MaxValue :
                PartialDateTime.Parse(value.End);

            yield return new DateTimeSearchValue(start, end);
        }
    }
}

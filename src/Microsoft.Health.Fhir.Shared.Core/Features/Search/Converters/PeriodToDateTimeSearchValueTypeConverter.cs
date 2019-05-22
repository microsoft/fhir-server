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
    /// <summary>
    /// A converter used to convert from <see cref="Period"/> to a list of <see cref="DateTimeSearchValue"/>.
    /// </summary>
    public class PeriodToDateTimeSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Period, DateTimeSearchValue>
    {
        protected override IEnumerable<DateTimeSearchValue> ConvertTo(Period value)
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

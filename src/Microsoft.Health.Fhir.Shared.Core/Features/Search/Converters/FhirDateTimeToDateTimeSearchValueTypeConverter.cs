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
    /// A converter used to convert from <see cref="FhirDateTime"/> to a list of <see cref="DateTimeSearchValue"/>.
    /// </summary>
    public class FhirDateTimeToDateTimeSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<FhirDateTime, DateTimeSearchValue>
    {
        protected override IEnumerable<DateTimeSearchValue> ConvertTo(FhirDateTime value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new DateTimeSearchValue(Models.PartialDateTime.Parse(value.Value));
        }
    }
}

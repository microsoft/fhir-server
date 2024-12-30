// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class TimingToDateTimeSearchValueConverter : FhirTypedElementToSearchValueConverter<DateTimeSearchValue>
    {
        public TimingToDateTimeSearchValueConverter()
            : base("Timing")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string stringValue = value.Value?.ToString();

            if (stringValue == null)
            {
                yield break;
            }

            var val = PrimitiveTypeConverter.ConvertTo<DateTimeOffset>(stringValue);

            yield return new DateTimeSearchValue(val);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Instant"/> to a list of <see cref="DateTimeSearchValue"/>.
    /// </summary>
    public class InstantNodeToDateTimeSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<DateTimeSearchValue>
    {
        public override string FhirNodeType { get; } = "instant";

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

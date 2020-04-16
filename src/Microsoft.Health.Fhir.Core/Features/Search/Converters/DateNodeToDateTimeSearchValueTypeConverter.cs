// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Date"/> to a list of <see cref="DateTimeSearchValue"/>.
    /// </summary>
    public class DateNodeToDateTimeSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<DateTimeSearchValue>
    {
        public override string FhirNodeType { get; } = "date";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string stringValue = value.Value?.ToString();

            if (stringValue == null)
            {
                yield break;
            }

            yield return new DateTimeSearchValue(Models.PartialDateTime.Parse(stringValue));
        }
    }
}

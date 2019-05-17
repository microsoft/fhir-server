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
    /// A converter used to convert from <see cref="FhirUri"/> to a list of <see cref="UriSearchValue"/>.
    /// </summary>
    public class FhirUriToUriSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<FhirUri, UriSearchValue>
    {
        protected override IEnumerable<UriSearchValue> ConvertTo(FhirUri value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new UriSearchValue(value.Value);
        }
    }
}

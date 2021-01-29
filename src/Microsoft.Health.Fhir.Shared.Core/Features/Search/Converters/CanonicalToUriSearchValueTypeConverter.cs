// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#if !Stu3
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Canonical"/> to a list of <see cref="UriSearchValue"/>.
    /// </summary>
    public class CanonicalToUriSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Canonical, UriSearchValue>
    {
        protected override IEnumerable<UriSearchValue> ConvertTo(Canonical value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new UriSearchValue(value.Value, true);
        }
    }
}
#endif

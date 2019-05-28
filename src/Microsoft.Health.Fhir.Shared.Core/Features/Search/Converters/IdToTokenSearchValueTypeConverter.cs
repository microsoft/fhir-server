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
    /// A converter used to convert from <see cref="Id"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class IdToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Id, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(Id value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new TokenSearchValue(null, value.Value, null);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="ContactPoint"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class ContactPointToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<ContactPoint, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(ContactPoint value)
        {
            if (string.IsNullOrWhiteSpace(value.Value))
            {
                yield break;
            }

            yield return new TokenSearchValue(value.Use?.GetLiteral(), value.Value, null);
        }
    }
}

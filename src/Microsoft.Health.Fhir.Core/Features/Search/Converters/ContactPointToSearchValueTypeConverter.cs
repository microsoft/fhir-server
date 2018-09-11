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
    public class ContactPointToSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<ContactPoint>
    {
        protected override IEnumerable<ISearchValue> ConvertTo(ContactPoint value)
        {
            if (string.IsNullOrWhiteSpace(value.Value))
            {
                yield break;
            }

            yield return new TokenSearchValue(value.Use?.GetLiteral(), value.Value, null);
        }
    }
}

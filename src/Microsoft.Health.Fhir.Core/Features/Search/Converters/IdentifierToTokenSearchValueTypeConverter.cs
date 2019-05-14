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
    /// A converter used to convert from <see cref="Identifier"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class IdentifierToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Identifier, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(Identifier value)
        {
            if (string.IsNullOrEmpty(value.Value))
            {
                yield break;
            }

            // Based on spec: http://hl7.org/fhir/STU3/search.html#token,
            // the text for identifier is specified by Identifier.type.text.
            yield return new TokenSearchValue(value.System, value.Value, value.Type?.Text);
        }
    }
}

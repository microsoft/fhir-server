// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="FhirBoolean"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class FhirBooleanToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<FhirBoolean, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(FhirBoolean value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new TokenSearchValue(SpecialValues.System, value.Value.Value ? "true" : "false", null);
        }
    }
}

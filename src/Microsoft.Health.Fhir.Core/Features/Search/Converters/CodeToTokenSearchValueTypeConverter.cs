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
    /// A converter used to convert from <see cref="Code"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Code, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(Code value)
        {
            // From spec: http://hl7.org/fhir/STU3/terminologies.html#4.1
            // The instance represents the code only.
            // The system is implicit - it is defined as part of
            // the definition of the element, and not carried in the instance.
            yield return new TokenSearchValue(null, value.Value, null);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Code"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeNodeToTokenSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<TokenSearchValue>
    {
        public override string FhirNodeType { get; } = "code";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string code = value.Scalar("code") as string ?? value.Value as string;
            var system = value.Scalar("system") as string;

            // From spec: http://hl7.org/fhir/terminologies.html#4.1
            // The instance represents the code only.
            // The system is implicit - it is defined as part of
            // the definition of the element, and not carried in the instance.

            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(system))
            {
                yield return null;
                yield break;
            }

            yield return new TokenSearchValue(system, code, null);
        }
    }
}

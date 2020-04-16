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
    /// A converter used to convert from <see cref="Id"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class IdNodeToTokenSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<TokenSearchValue>
    {
        public override string FhirNodeType { get; } = "id";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string stringValue = value.Value as string;
            if (stringValue == null)
            {
                yield break;
            }

            yield return new TokenSearchValue(null, stringValue, null);
        }
    }
}

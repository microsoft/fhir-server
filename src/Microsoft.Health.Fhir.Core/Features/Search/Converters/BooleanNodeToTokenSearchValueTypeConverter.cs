// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="FhirBoolean"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class BooleanNodeToTokenSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<TokenSearchValue>
    {
        public override string FhirNodeType { get; } = "boolean";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            object fhirValue = value.Value;

            if (fhirValue == null)
            {
                yield break;
            }

            yield return new TokenSearchValue(SpecialValues.System, (bool)fhirValue ? "true" : "false", null);
        }
    }
}

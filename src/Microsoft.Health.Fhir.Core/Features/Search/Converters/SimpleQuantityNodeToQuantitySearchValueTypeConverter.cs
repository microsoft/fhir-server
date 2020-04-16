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
    /// A converter used to convert from <see cref="SimpleQuantity"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class SimpleQuantityNodeToQuantitySearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<QuantitySearchValue>
    {
        public override string FhirNodeType { get; } = "SimpleQuantity";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var val = (decimal?)value.Scalar("value");

            if (val == null)
            {
                yield break;
            }

            var system = value.Scalar("system") as string;
            var code = value.Scalar("code") as string;

            yield return new QuantitySearchValue(
                system,
                code,
                val.GetValueOrDefault());
        }
    }
}

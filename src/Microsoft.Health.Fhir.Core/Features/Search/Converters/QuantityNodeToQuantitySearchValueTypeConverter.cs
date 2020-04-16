// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model.Primitives;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Quantity"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class QuantityNodeToQuantitySearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<QuantitySearchValue>
    {
        public override string FhirNodeType { get; } = "Quantity";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var decimalValue = (decimal?)value.Scalar("value");

            if (decimalValue == null)
            {
                yield break;
            }

            var system = value.Scalar("system")?.ToString();
            var code = value.Scalar("code")?.ToString();

            yield return new QuantitySearchValue(
                system,
                code,
                decimalValue.GetValueOrDefault());
        }
    }
}

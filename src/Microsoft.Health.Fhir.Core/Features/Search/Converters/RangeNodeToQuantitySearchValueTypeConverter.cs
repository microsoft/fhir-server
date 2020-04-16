// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Range"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class RangeNodeToQuantitySearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<QuantitySearchValue>
    {
        public override string FhirNodeType { get; } = "Range";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var highValue = (ITypedElement)value.Scalar("high");
            var lowValue = (ITypedElement)value.Scalar("low");

            var quantityRepresentativeValue = lowValue ?? highValue;
            var system = quantityRepresentativeValue.Scalar("system") as string;
            var code = quantityRepresentativeValue.Scalar("code") as string;

            if (quantityRepresentativeValue != null)
            {
                // FROM https://www.hl7.org/fhir/datatypes.html#Range: "The unit and code/system elements of the low or high elements SHALL match."

                yield return new QuantitySearchValue(system, code, (decimal?)lowValue.Scalar("value"), (decimal?)highValue.Scalar("value"));
            }
        }
    }
}

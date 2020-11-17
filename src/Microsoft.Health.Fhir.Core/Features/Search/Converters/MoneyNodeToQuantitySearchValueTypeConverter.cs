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
    public class MoneyNodeToQuantitySearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<QuantitySearchValue>
    {
        public MoneyNodeToQuantitySearchValueTypeConverter()
            : base("Money")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var decimalValue = (decimal?)value.Scalar("value");

            if (decimalValue == null)
            {
                yield break;
            }

            var code = value.Scalar("currency")?.ToString(); // TODO: Do we need to check that this is a legit currency?

            // TODO: This cannot be null, right?
            if (code == null)
            {
                yield break;
            }

            yield return new QuantitySearchValue(
                null,
                code,
                decimalValue.GetValueOrDefault());
        }
    }
}

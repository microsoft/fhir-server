// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Money"/> to a list of <see cref="QuantitySearchValue"/>.
    /// </summary>
    public class MoneyToQuantitySearchValueConverter : FhirTypedElementToSearchValueConverter<QuantitySearchValue>
    {
        public MoneyToQuantitySearchValueConverter()
            : base("Money")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var decimalValue = (decimal?)value.Scalar("value");

            if (!decimalValue.HasValue)
            {
                yield break;
            }

            if (ModelInfoProvider.Version == FhirSpecification.Stu3)
            {
                var code = value.Scalar("code")?.ToString();
                var system = value.Scalar("system")?.ToString();

                // The spec specifies that only the code value must be provided.
                if (code == null)
                {
                    yield break;
                }

                yield return new QuantitySearchValue(
                    system,
                    code,
                    decimalValue.GetValueOrDefault());
            }
            else
            {
                var currency = value.Scalar("currency")?.ToString();

                if (currency == null)
                {
                    yield break;
                }

                yield return new QuantitySearchValue(
                    CurrencyValueSet.CodeSystemUri, // TODO: Use ICodeSystemResolver to pull this from resourcepath-codesystem-mappings.json once it's added.
                    currency,
                    decimalValue.GetValueOrDefault());
            }
        }
    }
}

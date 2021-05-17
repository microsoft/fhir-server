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
    /// A converter used to convert from <see cref="Integer"/> to a list of <see cref="NumberSearchValue"/>.
    /// </summary>
    public class IntegerToNumberSearchValueConverter : FhirTypedElementToSearchValueConverter<NumberSearchValue>
    {
        public IntegerToNumberSearchValueConverter()
            : base("integer", "positiveInt", "unsignedInt", "System.Integer")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new NumberSearchValue((int)value.Value);
        }
    }
}

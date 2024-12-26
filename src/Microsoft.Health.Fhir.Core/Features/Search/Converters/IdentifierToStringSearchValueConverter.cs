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
    public class IdentifierToStringSearchValueConverter : FhirTypedElementToSearchValueConverter<StringSearchValue>
    {
        public IdentifierToStringSearchValueConverter()
            : base("Identifier")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var s = value?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                yield return new StringSearchValue(s);
            }
        }
    }
}

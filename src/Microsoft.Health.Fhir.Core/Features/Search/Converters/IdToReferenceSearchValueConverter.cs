// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class IdToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        public IdToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
            : base("id")
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _referenceSearchValueParser = referenceSearchValueParser;
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var id = value?.Value as string;
            if (id == null)
            {
                yield break;
            }

            yield return _referenceSearchValueParser.Parse(id);
        }
    }
}

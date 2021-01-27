// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Uri"/> to a list of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class UriToReferenceSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<FhirUri, ReferenceSearchValue>
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        public UriToReferenceSearchValueTypeConverter(IReferenceSearchValueParser referenceSearchValueParser)
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _referenceSearchValueParser = referenceSearchValueParser;
        }

        protected override IEnumerable<ReferenceSearchValue> ConvertTo(FhirUri value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            var reference = new UriSearchValue(value.Value, false);

            // Contained resources will not be searchable.
            if (reference.Uri.StartsWith("#", StringComparison.Ordinal)
                || reference.Uri.StartsWith("urn:", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return _referenceSearchValueParser.Parse(reference.Uri);
        }
    }
}

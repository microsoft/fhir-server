// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Uri"/> to a list of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class UriToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        public UriToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
            : base("uri", "url")
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _referenceSearchValueParser = referenceSearchValueParser;
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string uri = value.Value?.ToString();

            if (uri == null)
            {
                yield break;
            }

            // Contained resources will not be searchable.
            if (uri.StartsWith('#')
                || uri.StartsWith("urn:", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return _referenceSearchValueParser.Parse(uri);
        }
    }
}

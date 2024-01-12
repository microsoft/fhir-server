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
    /// <summary>
    /// A converter used to convert from <see cref="ResourceReference"/> to a list of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class ResourceReferenceToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        public ResourceReferenceToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
            : base("Reference")
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _referenceSearchValueParser = referenceSearchValueParser;
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var reference = value.Scalar("reference") as string;

            if (reference == null)
            {
                yield break;
            }

            // Contained resources will not be searchable.
            if (reference.StartsWith('#')
                || reference.StartsWith("urn:", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return _referenceSearchValueParser.Parse(reference);
        }
    }
}

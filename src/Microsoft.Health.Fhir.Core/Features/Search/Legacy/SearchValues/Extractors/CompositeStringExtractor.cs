// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal class CompositeStringExtractor<TResource> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, CodeableConcept> _compositeTokenSelector;
        private Func<TResource, FhirString> _stringSelector;

        public CompositeStringExtractor(
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, FhirString> stringSelector)
        {
            EnsureArg.IsNotNull(compositeTokenSelector, nameof(compositeTokenSelector));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            _compositeTokenSelector = compositeTokenSelector;
            _stringSelector = stringSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<LegacyCompositeSearchValue>();

            string value = _stringSelector(resource)?.Value;

            if (!string.IsNullOrEmpty(value))
            {
                var stringSearchValue = new StringSearchValue(value);

                IEnumerable<Coding> codings = _compositeTokenSelector.ExtractNonEmptyCoding(resource);

                results.AddRange(
                    codings.Select(coding => new LegacyCompositeSearchValue(
                        coding.System,
                        coding.Code,
                        stringSearchValue)));
            }

            return results;
        }
    }
}

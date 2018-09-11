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
    internal class CompositeDateTimeExtractor<TResource> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, CodeableConcept> _compositeTokenSelector;
        private Func<TResource, FhirDateTime> _dateTimeSelector;

        public CompositeDateTimeExtractor(
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, FhirDateTime> dateTimeSelector)
        {
            EnsureArg.IsNotNull(compositeTokenSelector, nameof(compositeTokenSelector));
            EnsureArg.IsNotNull(dateTimeSelector, nameof(dateTimeSelector));

            _compositeTokenSelector = compositeTokenSelector;
            _dateTimeSelector = dateTimeSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<LegacyCompositeSearchValue>();

            FhirDateTime dateTime = _dateTimeSelector(resource);

            if (dateTime != null)
            {
                DateTimeSearchValue value = DateTimeSearchValue.Parse(dateTime.Value);

                IEnumerable<Coding> codings = _compositeTokenSelector.ExtractNonEmptyCoding(resource);

                results.AddRange(
                    codings.Select(coding => new LegacyCompositeSearchValue(
                        coding.System,
                        coding.Code,
                        value)));
            }

            return results;
        }
    }
}

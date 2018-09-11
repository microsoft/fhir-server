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
    internal class CompositeTokenExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, CodeableConcept> _compositeTokenSelector;
        private Func<TCollection, CodeableConcept> _tokenSelector;

        public CompositeTokenExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> compositeTokenSelector,
            Func<TCollection, CodeableConcept> tokenSelector)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(compositeTokenSelector, nameof(compositeTokenSelector));
            EnsureArg.IsNotNull(tokenSelector, nameof(tokenSelector));

            _collectionSelector = collectionSelector;
            _compositeTokenSelector = compositeTokenSelector;
            _tokenSelector = tokenSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<LegacyCompositeSearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                IEnumerable<Coding> compositeCodings = _compositeTokenSelector.ExtractNonEmptyCoding(item);
                IEnumerable<Coding> tokenCodings = _tokenSelector.ExtractNonEmptyCoding(item);

                results.AddRange(compositeCodings.SelectMany(compositeCoding => tokenCodings
                    .Select(tokenCoding => new LegacyCompositeSearchValue(
                        compositeCoding.System,
                        compositeCoding.Code,
                        new TokenSearchValue(tokenCoding.System, tokenCoding.Code, tokenCoding.Display)))));
            }

            return results;
        }
    }
}

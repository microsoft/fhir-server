// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal class ReferenceExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, ResourceReference> _referenceSelector;
        private FHIRAllTypes? _resourceTypeFilter;

        public ReferenceExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, ResourceReference> referenceSelector,
            FHIRAllTypes? resourceTypeFilter = null)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(referenceSelector, nameof(referenceSelector));

            _collectionSelector = collectionSelector;
            _referenceSelector = referenceSelector;
            _resourceTypeFilter = resourceTypeFilter;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<ReferenceSearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                ResourceReference reference = _referenceSelector(item);

                if (reference != null &&
                    !reference.IsEmpty() &&
                    !reference.IsContainedReference &&
                    (_resourceTypeFilter == null || reference.IsReferenceTypeOf(_resourceTypeFilter.Value)))
                {
                    results.Add(new ReferenceSearchValue(reference.Reference));
                }
            }

            return results;
        }
    }
}

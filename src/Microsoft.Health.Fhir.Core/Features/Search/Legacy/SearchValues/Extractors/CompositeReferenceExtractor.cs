// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal class CompositeReferenceExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, Enum> _enumSelector;
        private Func<TCollection, ResourceReference> _referenceSelector;

        public CompositeReferenceExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Enum> enumSelector,
            Func<TCollection, ResourceReference> referenceSelector)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(enumSelector, nameof(enumSelector));
            EnsureArg.IsNotNull(referenceSelector, nameof(referenceSelector));

            _collectionSelector = collectionSelector;
            _enumSelector = enumSelector;
            _referenceSelector = referenceSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<LegacyCompositeSearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                Enum enumValue = _enumSelector(item);

                string system = enumValue?.GetSystem();
                string code = enumValue?.GetLiteral();

                ResourceReference reference = _referenceSelector(item);

                if (!string.IsNullOrEmpty(code) &&
                    reference != null &&
                    !reference.IsEmpty() &&
                    !reference.IsContainedReference)
                {
                    var referenceSearchValue = ReferenceSearchValue.Parse(reference.Reference);
                    var compositeSearchValue = new LegacyCompositeSearchValue(
                        system,
                        code,
                        referenceSearchValue);

                    results.Add(compositeSearchValue);
                }
            }

            return results;
        }
    }
}

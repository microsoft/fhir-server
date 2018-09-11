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
    internal class QuantityExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, Quantity> _quantitySelector;

        public QuantityExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Quantity> quantitySelector)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(quantitySelector, nameof(quantitySelector));

            _collectionSelector = collectionSelector;
            _quantitySelector = quantitySelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<QuantitySearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                Quantity quantity = _quantitySelector(item);

                if (quantity != null && !quantity.IsEmpty())
                {
                    results.Add(new QuantitySearchValue(
                        quantity.System,
                        quantity.Code,
                        quantity.Value.Value));
                }
            }

            return results;
        }
    }
}

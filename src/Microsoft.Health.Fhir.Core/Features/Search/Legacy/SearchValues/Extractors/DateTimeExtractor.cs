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
    internal class DateTimeExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, string> _dateTimeStartSelector;
        private Func<TCollection, string> _dateTimeEndSelector;

        public DateTimeExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> dateTimeStartSelector,
            Func<TCollection, string> dateTimeEndSelector)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(dateTimeStartSelector, nameof(dateTimeStartSelector));
            EnsureArg.IsNotNull(dateTimeEndSelector, nameof(dateTimeEndSelector));

            _collectionSelector = collectionSelector;
            _dateTimeStartSelector = dateTimeStartSelector;
            _dateTimeEndSelector = dateTimeEndSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<DateTimeSearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                string startDate = _dateTimeStartSelector(item);
                string endDate = _dateTimeEndSelector(item);

                if (string.IsNullOrWhiteSpace(startDate) && string.IsNullOrWhiteSpace(endDate))
                {
                    continue;
                }

                // If the start time or end time is not supplied, then set it to the min or max value respectively.
                // This allows search to operate on the knowledge that there is always a start and end to a time.
                results.Add(
                    DateTimeSearchValue.Parse(
                        string.IsNullOrWhiteSpace(startDate) ? DateTimeOffset.MinValue.ToString("o") : startDate,
                        string.IsNullOrWhiteSpace(endDate) ? DateTimeOffset.MaxValue.ToString("o") : endDate));
            }

            return results;
        }
    }
}

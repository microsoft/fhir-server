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
    internal class NumberExtractor<TResource> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<decimal?>> _numbersSelector;

        public NumberExtractor(
            Func<TResource, IEnumerable<decimal?>> numbersSelector)
        {
            EnsureArg.IsNotNull(numbersSelector, nameof(numbersSelector));

            _numbersSelector = numbersSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<NumberSearchValue>();

            IEnumerable<decimal?> values = _numbersSelector(resource)?
                .Where(item => item.HasValue) ??
                Enumerable.Empty<decimal?>();

            results.AddRange(values.Select(number => new NumberSearchValue(number.Value)));

            return results;
        }
    }
}

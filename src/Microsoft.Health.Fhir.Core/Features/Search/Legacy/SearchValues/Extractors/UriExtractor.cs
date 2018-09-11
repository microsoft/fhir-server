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
    internal class UriExtractor<TResource> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<string>> _urisSelector;

        internal UriExtractor(
            Func<TResource, IEnumerable<string>> urisSelector)
        {
            EnsureArg.IsNotNull(urisSelector, nameof(urisSelector));

            _urisSelector = urisSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<UriSearchValue>();

            IEnumerable<string> collection = _urisSelector(resource);

            if (collection != null)
            {
                results.AddRange(collection
                    .Where(item => item != null)
                    .Select(item => new UriSearchValue(item)));
            }

            return results;
        }
    }
}

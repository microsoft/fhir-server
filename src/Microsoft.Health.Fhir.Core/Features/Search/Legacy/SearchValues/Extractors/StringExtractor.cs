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
    internal class StringExtractor<TResource> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<string>> _stringsSelector;

        public StringExtractor(
            Func<TResource, IEnumerable<string>> stringsSelector)
        {
            EnsureArg.IsNotNull(stringsSelector, nameof(stringsSelector));

            _stringsSelector = stringsSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<StringSearchValue>();

            IEnumerable<string> values = _stringsSelector(resource)?
                .Where(item => !string.IsNullOrEmpty(item)) ??
                Enumerable.Empty<string>();

            results.AddRange(values.Select(date => StringSearchValue.Parse(date)));

            return results;
        }
    }
}

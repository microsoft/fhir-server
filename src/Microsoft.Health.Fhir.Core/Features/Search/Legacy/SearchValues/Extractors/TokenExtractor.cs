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
    internal class TokenExtractor<TResource, TCollection> : SearchValuesExtractor<TResource>
        where TResource : Resource
    {
        private Func<TResource, IEnumerable<TCollection>> _collectionSelector;
        private Func<TCollection, string> _systemSelector;
        private Func<TCollection, string> _codeSelector;
        private Func<TCollection, string> _textSelector;

        public TokenExtractor(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> systemSelector,
            Func<TCollection, string> codeSelector,
            Func<TCollection, string> textSelector = null)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(systemSelector, nameof(systemSelector));
            EnsureArg.IsNotNull(codeSelector, nameof(codeSelector));

            _collectionSelector = collectionSelector;
            _systemSelector = systemSelector;
            _codeSelector = codeSelector;
            _textSelector = textSelector;
        }

        internal override IReadOnlyCollection<ISearchValue> Extract(TResource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var results = new List<TokenSearchValue>();

            IEnumerable<TCollection> collection = _collectionSelector.ExtractNonEmptyCollection(resource);

            foreach (TCollection item in collection)
            {
                string system = _systemSelector(item);
                string code = _codeSelector(item);

                string text = null;

                if (_textSelector != null)
                {
                    text = _textSelector(item);
                }

                if (!string.IsNullOrWhiteSpace(system) ||
                    !string.IsNullOrWhiteSpace(code) ||
                    !string.IsNullOrWhiteSpace(text))
                {
                    results.Add(new TokenSearchValue(system, code, text));
                }
            }

            return results;
        }
    }
}

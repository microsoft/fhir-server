// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal static class SelectorExtensions
    {
        internal static IEnumerable<TCollection> ExtractNonEmptyCollection<TResource, TCollection>(
            this Func<TResource, IEnumerable<TCollection>> collectionSelector,
            TResource resource)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));

            return collectionSelector(resource)?
                .Where(item => item != null) ??
                Enumerable.Empty<TCollection>();
        }

        internal static IEnumerable<Coding> ExtractNonEmptyCoding<TCollection>(
            this Func<TCollection, IEnumerable<Coding>> collectionSelector,
            TCollection collection)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));

            return collectionSelector(collection)?
                .Where(coding => coding != null && !coding.IsEmpty()) ??
                Enumerable.Empty<Coding>();
        }

        internal static IEnumerable<Coding> ExtractNonEmptyCoding<TCollection>(
            this Func<TCollection, IEnumerable<CodeableConcept>> collectionSelector,
            TCollection collection)
        {
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));

            return collectionSelector(collection)?
                .SelectMany(c => c.Coding)
                .Where(coding => coding != null && !coding.IsEmpty()) ??
                Enumerable.Empty<Coding>();
        }

        internal static IEnumerable<Coding> ExtractNonEmptyCoding<TInput>(
            this Func<TInput, CodeableConcept> compositeTokenSelector,
            TInput input)
        {
            EnsureArg.IsNotNull(compositeTokenSelector, nameof(compositeTokenSelector));

            return compositeTokenSelector(input)?.Coding?
                .Where(coding => coding != null && !coding.IsEmpty()) ??
                Enumerable.Empty<Coding>();
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public struct SearchResultEntry : IEquatable<SearchResultEntry>
    {
        public SearchResultEntry(
            ResourceWrapper resourceWrapper,
            SearchEntryMode searchEntryMode = SearchEntryMode.Match,
            decimal? score = null)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            Resource = resourceWrapper;
            SearchEntryMode = searchEntryMode;
            Score = score;
        }

        public ResourceWrapper Resource { get; }

        public SearchEntryMode SearchEntryMode { get; }

        /// <summary>
        /// Gets the normalized semantic relevance score, where higher is more relevant.
        /// </summary>
        public decimal? Score { get; }

        public static bool operator ==(SearchResultEntry left, SearchResultEntry right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SearchResultEntry left, SearchResultEntry right)
        {
            return !(left == right);
        }

        public bool Equals(SearchResultEntry other)
        {
            return Equals(Resource, other.Resource) && SearchEntryMode == other.SearchEntryMode;
        }

        public override bool Equals(object obj)
        {
            return obj is SearchResultEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Resource != null ? Resource.GetHashCode() : 0) * 397) ^ (int)SearchEntryMode;
            }
        }
    }
}

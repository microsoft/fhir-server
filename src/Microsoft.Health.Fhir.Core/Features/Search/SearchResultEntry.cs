// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public struct SearchResultEntry : IEquatable<SearchResultEntry>
    {
        public SearchResultEntry(
            ResourceWrapper resourceWrapper,
            SearchEntryMode searchEntryMode = SearchEntryMode.Match,
            decimal? score = null,
            SemanticSearchEvidence evidence = null,
            IReadOnlyList<SemanticSearchEvidence> evidenceItems = null)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            Resource = resourceWrapper;
            SearchEntryMode = searchEntryMode;
            Score = score;
            EvidenceItems = evidenceItems ?? (evidence == null ? Array.Empty<SemanticSearchEvidence>() : new[] { evidence });
            Evidence = EvidenceItems.Count > 0 ? EvidenceItems[0] : null;
        }

        public ResourceWrapper Resource { get; }

        public SearchEntryMode SearchEntryMode { get; }

        /// <summary>
        /// Gets the normalized semantic relevance score, where higher is more relevant.
        /// </summary>
        public decimal? Score { get; }

        /// <summary>
        /// Gets the exact passage and provenance supporting this semantic result.
        /// </summary>
        public SemanticSearchEvidence Evidence { get; }

        /// <summary>
        /// Gets the supporting passages ordered by relevance within this resource.
        /// </summary>
        public IReadOnlyList<SemanticSearchEvidence> EvidenceItems { get; }

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

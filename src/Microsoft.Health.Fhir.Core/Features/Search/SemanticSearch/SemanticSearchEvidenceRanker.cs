// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Assigns page-scoped ranks to semantic evidence without changing resource or evidence order.
    /// </summary>
    public static class SemanticSearchEvidenceRanker
    {
        /// <summary>
        /// Assigns dense one-based ranks across all evidence attached to resources on the current response page.
        /// </summary>
        /// <param name="evidenceByResource">Evidence grouped in response resource order and relevance order.</param>
        /// <returns>Evidence in the original grouping and order with ranks assigned.</returns>
        public static IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> AssignRanks(
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> evidenceByResource)
        {
            EnsureArg.IsNotNull(evidenceByResource, nameof(evidenceByResource));

            int[][] ranks = evidenceByResource
                .Select(evidenceItems => new int[evidenceItems.Count])
                .ToArray();
            int rank = 1;

            foreach (var item in evidenceByResource
                .SelectMany((evidenceItems, resourceIndex) => evidenceItems.Select((evidence, evidenceIndex) => new
                {
                    Evidence = evidence,
                    ResourceIndex = resourceIndex,
                    EvidenceIndex = evidenceIndex,
                }))
                .OrderByDescending(item => item.Evidence.Score ?? decimal.MinValue)
                .ThenBy(item => item.ResourceIndex)
                .ThenBy(item => item.EvidenceIndex))
            {
                ranks[item.ResourceIndex][item.EvidenceIndex] = rank++;
            }

            return evidenceByResource
                .Select((evidenceItems, resourceIndex) =>
                    (IReadOnlyList<SemanticSearchEvidence>)evidenceItems
                        .Select((evidence, evidenceIndex) => evidence.WithRank(ranks[resourceIndex][evidenceIndex]))
                        .ToList())
                .ToList();
        }
    }
}

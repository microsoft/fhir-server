// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class FilterCriteriaOutcome
    {
        public FilterCriteriaOutcome(bool match, OperationOutcomeIssue outcomeIssue)
        {
            if (!match)
            {
                // If the outcome does not match, then the outcome issue is required.
                EnsureArg.IsNotNull(outcomeIssue);
            }

            Match = match;
            OutcomeIssue = outcomeIssue;
        }

        public static FilterCriteriaOutcome MatchingOutcome => new FilterCriteriaOutcome(match: true, outcomeIssue: null);

        public OperationOutcomeIssue OutcomeIssue { get; }

        public bool Match { get; }
    }
}

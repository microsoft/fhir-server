// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Identifies the search warnings for valid values that the data store compares only in part
    /// (<see cref="LongUriSearchValueIssue"/> and <see cref="LongReferenceIdIssue"/>).
    /// These are documented limitations, not invalid search parameters, so operations that reject
    /// searches with issues (for example bulk delete) still run and return them as warnings.
    /// </summary>
    internal static class PartiallyComparedSearchValueIssue
    {
        /// <summary>
        /// Returns whether <paramref name="issue"/> is one of these warnings.
        /// </summary>
        /// <param name="issue">The issue to check.</param>
        /// <returns><c>true</c> if the issue is one of these warnings; otherwise <c>false</c>.</returns>
        public static bool IsMatch(OperationOutcomeIssue issue)
            => LongUriSearchValueIssue.IsMatch(issue) || LongReferenceIdIssue.IsMatch(issue);
    }
}

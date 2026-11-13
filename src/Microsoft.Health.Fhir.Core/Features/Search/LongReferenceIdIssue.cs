// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// The warning returned when a reference contains a resource id longer than the maximum resource id length.
    /// Only the first maximum-length characters of referenced ids are indexed, so such a reference can match a different resource.
    /// The same warning is used for search values and for references in written resources.
    /// </summary>
    internal static class LongReferenceIdIssue
    {
        private static readonly string DiagnosticsPrefix = GetDiagnosticsPrefix();

        /// <summary>
        /// Creates the warning.
        /// </summary>
        /// <param name="searchParameterCode">The code of the affected search parameter.</param>
        /// <param name="maxResourceIdLength">The maximum resource id length.</param>
        /// <returns>The warning.</returns>
        public static OperationOutcomeIssue Create(string searchParameterCode, int maxResourceIdLength)
        {
            return new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Warning,
                OperationOutcomeConstants.IssueType.Informational,
                FormatDiagnostics(searchParameterCode, maxResourceIdLength));
        }

        /// <summary>
        /// Creates the issue returned in a background job status for references found while the job wrote resources.
        /// </summary>
        /// <param name="count">The number of affected resources.</param>
        /// <returns>The issue.</returns>
        public static OperationOutcomeIssue CreateForJob(OverlongReferenceIdCount count)
        {
            EnsureArg.IsNotNull(count, nameof(count));

            return new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Information,
                OperationOutcomeConstants.IssueType.Informational,
                diagnostics: FormatDiagnostics(count.SearchParameterCode, count.MaxResourceIdLength),
                detailsText: string.Format(CultureInfo.InvariantCulture, Core.Resources.LongReferenceIdAffectedResources, count.ResourceType, count.Count));
        }

        /// <summary>
        /// Adds the warning for a search parameter to <paramref name="issues"/>, unless the same warning is already there.
        /// </summary>
        /// <param name="issues">The issues to add to.</param>
        /// <param name="searchParameterCode">The code of the affected search parameter.</param>
        /// <param name="maxResourceIdLength">The maximum resource id length.</param>
        /// <returns><c>true</c> if the warning was added; <c>false</c> if it was already there.</returns>
        public static bool TryAdd(IList<OperationOutcomeIssue> issues, string searchParameterCode, int maxResourceIdLength)
        {
            EnsureArg.IsNotNull(issues, nameof(issues));

            OperationOutcomeIssue issue = Create(searchParameterCode, maxResourceIdLength);
            if (issues.Any(i => IsMatch(i) && string.Equals(i.Diagnostics, issue.Diagnostics, StringComparison.Ordinal)))
            {
                return false;
            }

            issues.Add(issue);
            return true;
        }

        /// <summary>
        /// Returns the search index entries whose reference, or reference component, has a resource id longer than the maximum.
        /// </summary>
        /// <param name="searchIndices">The search index entries of a resource.</param>
        /// <returns>Each affected entry with the overlong reference and the maximum resource id length.</returns>
        public static IEnumerable<(SearchIndexEntry Entry, ReferenceSearchValue Reference, int MaxResourceIdLength)> GetOverlongReferences(IEnumerable<SearchIndexEntry> searchIndices)
        {
            if (searchIndices == null)
            {
                yield break;
            }

            foreach (SearchIndexEntry entry in searchIndices)
            {
                IEnumerable<ISearchValue> values = entry.Value is CompositeSearchValue composite
                    ? composite.Components.SelectMany(component => component)
                    : [entry.Value];

                foreach (ReferenceSearchValue reference in values.OfType<ReferenceSearchValue>().Where(r => r.OverlongResourceIdLength != null && r.MaxResourceIdLength != null))
                {
                    yield return (entry, reference, reference.MaxResourceIdLength.GetValueOrDefault());
                }
            }
        }

        /// <summary>
        /// Returns whether <paramref name="issue"/> is this warning.
        /// </summary>
        /// <param name="issue">The issue to check.</param>
        /// <returns><c>true</c> if the issue is this warning; otherwise <c>false</c>.</returns>
        public static bool IsMatch(OperationOutcomeIssue issue)
        {
            return issue != null
                && string.Equals(issue.Code, OperationOutcomeConstants.IssueType.Informational, StringComparison.Ordinal)
                && (string.Equals(issue.Severity, OperationOutcomeConstants.IssueSeverity.Warning, StringComparison.Ordinal)
                    || string.Equals(issue.Severity, OperationOutcomeConstants.IssueSeverity.Information, StringComparison.Ordinal))
                && issue.Diagnostics?.StartsWith(DiagnosticsPrefix, StringComparison.Ordinal) == true;
        }

        private static string FormatDiagnostics(string searchParameterCode, int maxResourceIdLength)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterCode, nameof(searchParameterCode));

            return string.Format(CultureInfo.InvariantCulture, Core.Resources.LongReferenceId, searchParameterCode, maxResourceIdLength);
        }

        private static string GetDiagnosticsPrefix()
        {
            string format = Core.Resources.LongReferenceId;
            int index = format.IndexOf("{0}", StringComparison.Ordinal);
            return index > 0 ? format[..index] : format;
        }
    }
}

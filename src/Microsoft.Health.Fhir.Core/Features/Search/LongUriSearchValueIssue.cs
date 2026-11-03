// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// The search warning returned when a URI search value is too long for the data store to compare in full.
    /// </summary>
    internal static class LongUriSearchValueIssue
    {
        private static readonly string DiagnosticsPrefix = GetDiagnosticsPrefix();

        /// <summary>
        /// Creates the warning.
        /// </summary>
        /// <param name="searchParameterCodes">The codes of the affected search parameters.</param>
        /// <param name="comparedLength">The number of URI characters the data store compares.</param>
        /// <returns>The warning.</returns>
        public static OperationOutcomeIssue Create(IEnumerable<string> searchParameterCodes, int comparedLength)
        {
            EnsureArg.IsNotNull(searchParameterCodes, nameof(searchParameterCodes));

            string diagnostics = string.Format(
                CultureInfo.InvariantCulture,
                Core.Resources.LongUriSearchValue,
                string.Join(", ", searchParameterCodes.OrderBy(c => c, StringComparer.Ordinal)),
                comparedLength);

            return new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Warning,
                OperationOutcomeConstants.IssueType.Informational,
                diagnostics);
        }

        /// <summary>
        /// Returns whether <paramref name="issue"/> is this warning.
        /// </summary>
        /// <param name="issue">The issue to check.</param>
        /// <returns><c>true</c> if the issue is this warning; otherwise <c>false</c>.</returns>
        public static bool IsMatch(OperationOutcomeIssue issue)
        {
            return issue != null
                && string.Equals(issue.Severity, OperationOutcomeConstants.IssueSeverity.Warning, StringComparison.Ordinal)
                && string.Equals(issue.Code, OperationOutcomeConstants.IssueType.Informational, StringComparison.Ordinal)
                && issue.Diagnostics?.StartsWith(DiagnosticsPrefix, StringComparison.Ordinal) == true;
        }

        private static string GetDiagnosticsPrefix()
        {
            string format = Core.Resources.LongUriSearchValue;
            int index = format.IndexOf("{0}", StringComparison.Ordinal);
            return index > 0 ? format[..index] : format;
        }
    }
}

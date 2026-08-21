// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal sealed class QueryPlanSanitizationResult
    {
        internal QueryPlanSanitizationResult(string status, string xml, bool truncated, int originalLength, int sanitizedLength)
        {
            Status = status;
            Xml = xml;
            Truncated = truncated;
            OriginalLength = originalLength;
            SanitizedLength = sanitizedLength;
        }

        internal string Status { get; }

        internal string Xml { get; }

        internal bool Truncated { get; }

        /// <summary>
        /// Gets the length of the raw Showplan XML as read from Query Store.
        /// </summary>
        internal int OriginalLength { get; }

        /// <summary>
        /// Gets the length of the sanitized Showplan XML before truncation, or zero when sanitization did not produce a document.
        /// This is the value to compare against the field cap when <see cref="Truncated"/> is set.
        /// </summary>
        internal int SanitizedLength { get; }
    }
}

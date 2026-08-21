// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// Outcome of Showplan sanitization.
    /// </summary>
    /// <remarks>
    /// The constructor is private and instances are produced only through the factory methods below, because the
    /// payload can embed literal parameter values taken from patient data. "Not verified but populated" is therefore
    /// a PHI-leak shape and must be impossible to express: every failure factory forces <see cref="Xml"/> to null and
    /// the success factory refuses a null document. <see cref="Truncated"/> is derived rather than supplied so it can
    /// never disagree with the payload it describes.
    /// </remarks>
    internal sealed class QueryPlanSanitizationResult
    {
        private QueryPlanSanitizationResult(string status, string xml, bool truncated, int originalLength, int sanitizedLength)
        {
            Status = status;
            Xml = xml;
            Truncated = truncated;
            OriginalLength = originalLength;
            SanitizedLength = sanitizedLength;
        }

        /// <summary>
        /// Gets the sanitization outcome, one of the status constants on <see cref="QueryPlanSanitizer"/>.
        /// </summary>
        internal string Status { get; }

        /// <summary>
        /// Gets the sanitized and verified Showplan XML, or null when sanitization did not succeed.
        /// </summary>
        internal string Xml { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="Xml"/> was truncated to the field cap.
        /// </summary>
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

        /// <summary>
        /// Creates a successful result. The XML is required to be non-null and already verified free of parameter data.
        /// </summary>
        /// <param name="xml">The sanitized, verified and field-capped Showplan XML.</param>
        /// <param name="originalLength">The length of the raw Showplan XML as read from Query Store.</param>
        /// <param name="sanitizedLength">The length of the sanitized Showplan XML before truncation.</param>
        /// <returns>A result carrying the sanitized document.</returns>
        internal static QueryPlanSanitizationResult Sanitized(string xml, int originalLength, int sanitizedLength)
        {
            EnsureArg.IsNotNull(xml, nameof(xml));
            EnsureArg.IsGte(originalLength, 0, nameof(originalLength));
            EnsureArg.IsGte(sanitizedLength, xml.Length, nameof(sanitizedLength));

            return new QueryPlanSanitizationResult(
                QueryPlanSanitizer.SanitizedStatus,
                xml,
                sanitizedLength > xml.Length,
                originalLength,
                sanitizedLength);
        }

        /// <summary>
        /// Creates a result for a plan that Query Store did not supply any Showplan XML for.
        /// </summary>
        /// <returns>A result with null XML.</returns>
        internal static QueryPlanSanitizationResult PlanXmlUnavailable()
        {
            return new QueryPlanSanitizationResult(QueryPlanSanitizer.PlanXmlUnavailableStatus, null, false, 0, 0);
        }

        /// <summary>
        /// Creates a result for Showplan XML that could not be parsed.
        /// </summary>
        /// <param name="originalLength">The length of the raw Showplan XML as read from Query Store.</param>
        /// <returns>A result with null XML.</returns>
        internal static QueryPlanSanitizationResult InvalidXml(int originalLength)
        {
            EnsureArg.IsGte(originalLength, 0, nameof(originalLength));

            return new QueryPlanSanitizationResult(QueryPlanSanitizer.InvalidXmlStatus, null, false, originalLength, 0);
        }

        /// <summary>
        /// Creates a result for Showplan XML in which parameter data survived removal. The document is discarded.
        /// </summary>
        /// <param name="originalLength">The length of the raw Showplan XML as read from Query Store.</param>
        /// <param name="sanitizedLength">The length of the discarded document, retained so the loss is quantifiable.</param>
        /// <returns>A result with null XML.</returns>
        internal static QueryPlanSanitizationResult VerificationFailed(int originalLength, int sanitizedLength)
        {
            EnsureArg.IsGte(originalLength, 0, nameof(originalLength));
            EnsureArg.IsGte(sanitizedLength, 0, nameof(sanitizedLength));

            return new QueryPlanSanitizationResult(QueryPlanSanitizer.VerificationFailedStatus, null, false, originalLength, sanitizedLength);
        }
    }
}

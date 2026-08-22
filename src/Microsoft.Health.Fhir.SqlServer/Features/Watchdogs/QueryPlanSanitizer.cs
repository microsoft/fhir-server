// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    /// <summary>
    /// Removes Showplan parameter metadata, which can carry literal values taken from patient data, and verifies
    /// that none of it survived before the plan is allowed out of the process.
    /// </summary>
    internal static class QueryPlanSanitizer
    {
        internal const string SanitizedStatus = "Sanitized";
        internal const string PlanXmlUnavailableStatus = "PlanXmlUnavailable";
        internal const string InvalidXmlStatus = "InvalidXml";
        internal const string VerificationFailedStatus = "VerificationFailed";

        /// <summary>
        /// Removes parameter metadata from a Showplan document, verifies the removal, and caps the result.
        /// </summary>
        /// <param name="queryPlanXml">The raw Showplan XML as read from Query Store.</param>
        /// <param name="maxLength">The maximum length of the returned XML.</param>
        /// <returns>The sanitization outcome. The XML is null unless removal was verified to have succeeded.</returns>
        internal static QueryPlanSanitizationResult Sanitize(string queryPlanXml, int maxLength)
        {
            if (string.IsNullOrEmpty(queryPlanXml))
            {
                return QueryPlanSanitizationResult.PlanXmlUnavailable();
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                };

                XDocument document;
                using (var stringReader = new StringReader(queryPlanXml))
                using (var xmlReader = XmlReader.Create(stringReader, settings))
                {
                    document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
                }

                // A document with no root element is handled here and only here, because it is the one condition that
                // would otherwise both skip removal (there is nothing to descend from) and satisfy verification
                // (nothing sensitive is found in an empty tree), and so would emit the document verbatim. XDocument.Load
                // rejects a rootless document today, which makes this unreachable; a PHI boundary must nevertheless
                // have no condition under which sanitization is skipped and verification reports success.
                if (document.Root == null)
                {
                    return QueryPlanSanitizationResult.VerificationFailed(queryPlanXml.Length, 0);
                }

                var elements = document.Root.DescendantsAndSelf()
                    .Where(element => string.Equals(element.Name.LocalName, "ParameterList", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                elements.Remove();

                var attributes = document.Root.DescendantsAndSelf()
                    .Attributes()
                    .Where(attribute =>
                        string.Equals(attribute.Name.LocalName, "ParameterCompiledValue", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attribute.Name.LocalName, "ParameterRuntimeValue", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                attributes.Remove();

                var sanitizedXml = document.ToString(SaveOptions.DisableFormatting);
                var sanitizedLength = sanitizedXml.Length;

                // Verification re-inspects the tree after removal and fails closed. It is deliberately structural:
                // Showplan embeds the original SQL in StatementText, so a text scan would drop any plan whose own
                // query text happens to contain the literal string "ParameterList".
                if (ContainsSensitiveParameterData(document))
                {
                    return QueryPlanSanitizationResult.VerificationFailed(queryPlanXml.Length, sanitizedLength);
                }

                maxLength = Math.Max(0, maxLength);
                if (sanitizedXml.Length > maxLength)
                {
                    sanitizedXml = sanitizedXml.Substring(0, maxLength);
                }

                return QueryPlanSanitizationResult.Sanitized(sanitizedXml, queryPlanXml.Length, sanitizedLength);
            }
            catch (XmlException)
            {
                return QueryPlanSanitizationResult.InvalidXml(queryPlanXml.Length);
            }
        }

        private static bool ContainsSensitiveParameterData(XDocument document)
        {
            // Matching is by local name and namespace-agnostic, exactly as the removal above, so a Showplan namespace
            // change between SQL versions cannot let parameter data pass verification. The root is known non-null:
            // Sanitize fails a rootless document closed before reaching removal.
            return document.Root.DescendantsAndSelf().Any(element =>
                IsSensitiveName(element.Name.LocalName) ||
                element.Attributes().Any(attribute => IsSensitiveName(attribute.Name.LocalName)));
        }

        private static bool IsSensitiveName(string localName)
        {
            return string.Equals(localName, "ParameterList", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(localName, "ParameterCompiledValue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(localName, "ParameterRuntimeValue", StringComparison.OrdinalIgnoreCase);
        }
    }
}

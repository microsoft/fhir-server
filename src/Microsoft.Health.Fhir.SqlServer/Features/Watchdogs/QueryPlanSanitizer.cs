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
    internal static class QueryPlanSanitizer
    {
        internal const string SanitizedStatus = "Sanitized";
        internal const string PlanXmlUnavailableStatus = "PlanXmlUnavailable";
        internal const string InvalidXmlStatus = "InvalidXml";
        internal const string VerificationFailedStatus = "VerificationFailed";

        internal static QueryPlanSanitizationResult Sanitize(string queryPlanXml, int maxLength)
        {
            if (string.IsNullOrEmpty(queryPlanXml))
            {
                return new QueryPlanSanitizationResult(PlanXmlUnavailableStatus, null, false, 0, 0);
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

                var elements = document.Root?.DescendantsAndSelf()
                    .Where(element => string.Equals(element.Name.LocalName, "ParameterList", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                elements?.Remove();

                var attributes = document.Root?.DescendantsAndSelf()
                    .Attributes()
                    .Where(attribute =>
                        string.Equals(attribute.Name.LocalName, "ParameterCompiledValue", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attribute.Name.LocalName, "ParameterRuntimeValue", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                attributes?.Remove();

                var sanitizedXml = document.ToString(SaveOptions.DisableFormatting);
                var sanitizedLength = sanitizedXml.Length;
                if (ContainsSensitiveParameterData(sanitizedXml))
                {
                    return new QueryPlanSanitizationResult(VerificationFailedStatus, null, false, queryPlanXml.Length, sanitizedLength);
                }

                maxLength = Math.Max(0, maxLength);
                var truncated = sanitizedXml.Length > maxLength;
                if (truncated)
                {
                    sanitizedXml = sanitizedXml.Substring(0, maxLength);
                }

                return new QueryPlanSanitizationResult(SanitizedStatus, sanitizedXml, truncated, queryPlanXml.Length, sanitizedLength);
            }
            catch (XmlException)
            {
                return new QueryPlanSanitizationResult(InvalidXmlStatus, null, false, queryPlanXml.Length, 0);
            }
        }

        private static bool ContainsSensitiveParameterData(string xml)
        {
            return xml.Contains("ParameterList", StringComparison.OrdinalIgnoreCase) ||
                xml.Contains("ParameterCompiledValue", StringComparison.OrdinalIgnoreCase) ||
                xml.Contains("ParameterRuntimeValue", StringComparison.OrdinalIgnoreCase);
        }
    }
}

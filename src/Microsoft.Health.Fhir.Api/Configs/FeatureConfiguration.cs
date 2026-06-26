// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// UI related configuration.
    /// </summary>
    public class FeatureConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the UI is supported or not.
        /// </summary>
        public bool SupportsUI { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether XML is supported or not.
        /// </summary>
        public bool SupportsXml { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether anonymized export is enabled or not.
        /// </summary>
        public bool SupportsAnonymizedExport { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether per-request server configuration overrides are honored.
        /// <para>
        /// When enabled, a caller may override allow-listed server configuration values for a single request
        /// by supplying them either as query-string parameters prefixed with <c>_config.</c>
        /// (for example <c>?_config.EnableFhirDateContainment=true</c>) or as request headers prefixed with
        /// <c>X-FHIRServer-Config-</c> (for example <c>X-FHIRServer-Config-EnableFhirDateContainment: true</c>).
        /// </para>
        /// <para>
        /// This is a TEST-ONLY facility intended to let the end-to-end test suite exercise multiple server
        /// configuration states against a single deployed server. Allowing clients to change server behavior
        /// per request is a security risk, so it is disabled by default and must NEVER be enabled in a
        /// production deployment. It can be toggled via the configuration key
        /// <c>FhirServer:Features:SupportsRequestConfigurationOverrides</c> or the environment variable
        /// <c>FhirServer__Features__SupportsRequestConfigurationOverrides</c>.
        /// </para>
        /// </summary>
        public bool SupportsRequestConfigurationOverrides { get; set; }
    }
}

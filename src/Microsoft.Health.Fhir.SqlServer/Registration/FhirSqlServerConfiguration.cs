// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Registration
{
    public class FhirSqlServerConfiguration
    {
        public bool ReuseQueryPlans { get; set; } = false;

        /// <summary>
        /// Gets or sets whether independent SQL search chains, and root-level filters after a chain, intersect distinct predecessor source keys.
        /// Disabled by default; opt in via <c>FhirSqlServer:EnableChainSourceDeduplication=true</c>
        /// (or environment variable <c>FhirSqlServer__EnableChainSourceDeduplication=true</c>).
        /// This feature is likely to be removed before the end of 2026 due to unforeseen SQL optimization side effects.
        /// </summary>
        public bool EnableChainSourceDeduplication { get; set; } = false;

        public bool EnableQueryPlanReuseChecker { get; set; } = false;

        public double QueryPlanReuseCheckerSkewThreshold { get; set; } = 30.0;

        public double QueryPlanReuseCheckerRefreshPeriod { get; set; } = 3600.0;

        /// <summary>
        /// Optimizes full UTC-day equality on built-in, non-composite date search parameters
        /// with derived <c>TemporalType.Date</c>, including homogeneous date unions.
        /// Uses <c>DateTimeEnd</c> equality for day-precision stored dates. When
        /// <see cref="EnableFhirDateContainment"/> is false, a disjoint predicate-level OR retains
        /// overlapping month/year stored dates. No temporal <c>UNION ALL</c> is introduced.
        /// Requires both <c>FhirSqlServer:EnableScalarTemporalEqualityRewriter</c> and
        /// <c>FhirServer:CoreFeatures:EnableSearchParameterTemporalClassification</c>.
        /// Both default to false. Environment variables use double underscores in place of colons.
        /// Classification occurs during definition initialization. Restart after changing these settings.
        /// Without classification, logs a startup warning and retains existing SQL.
        /// Custom definitions and other temporal leaf types retain existing SQL and search semantics.
        /// Tracked alongside AB#191826.
        /// </summary>
        public bool EnableScalarTemporalEqualityRewriter { get; set; } = false;

        /// <summary>
        /// Gets or sets whether SQL date predicates use only StartDateTime for system-defined
        /// parameters whose complete property path resolves exclusively to FHIR instant.
        /// Stored instant endpoints are equal. No schema or index changes are required.
        /// Disabled by default and independent of the date containment and scalar equality settings.
        /// </summary>
        public bool EnableInstantDateTimeRewriter { get; set; } = false;

        /// <summary>
        /// When true, date/time <c>eq</c> equality uses the FHIR-spec containment form Core already emits
        /// (<c>DateTimeStart &gt;= lo AND DateTimeEnd &lt;= hi</c>) instead of the legacy overlap form, so no
        /// temporal <c>UNION ALL</c> is emitted. (<c>ap</c> is unaffected — Core emits it as spec overlap
        /// directly.) This is a search-result behavior change: a finer-precision query (e.g. an exact day)
        /// no longer matches a coarser stored value (e.g. a month or year). Disabled by default so
        /// out-of-the-box behavior matches the legacy overlap semantics; opt in via
        /// <c>FhirSqlServer:EnableFhirDateContainment=true</c> (or env var
        /// <c>FhirSqlServer__EnableFhirDateContainment=true</c>). Tracked alongside AB#191826.
        /// </summary>
        public bool EnableFhirDateContainment { get; set; } = false;
    }
}

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

        public bool EnableQueryPlanReuseChecker { get; set; } = false;

        public double QueryPlanReuseCheckerSkewThreshold { get; set; } = 30.0;

        public double QueryPlanReuseCheckerRefreshPeriod { get; set; } = 3600.0;

        /// <summary>
        /// When true (together with <see cref="EnableFhirDateContainment"/>),
        /// <see cref="Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.ScalarTemporalEqualityRewriter"/>
        /// collapses an exact-day equality on allow-listed scalar date parameters (currently <c>birthdate</c>)
        /// to a single End-only <c>DateTimeEnd</c> predicate — an index optimization with no temporal
        /// <c>UNION ALL</c>. Gated on both flags because the End-only form matches the legacy overlap result
        /// only under containment. Disabled by default; opt in via
        /// <c>FhirSqlServer:EnableScalarTemporalEqualityRewriter=true</c> (or env var
        /// <c>FhirSqlServer__EnableScalarTemporalEqualityRewriter=true</c>). Tracked alongside AB#191826.
        /// </summary>
        public bool EnableScalarTemporalEqualityRewriter { get; set; } = false;

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

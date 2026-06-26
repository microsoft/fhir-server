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
        /// is included in the search-expression pipeline so allow-listed scalar date parameters
        /// (currently <c>birthdate</c>) collapse an exact-day equality to a single End-only
        /// <c>DateTimeEnd</c> predicate (an index optimization). It no longer emits a temporal
        /// <c>UNION ALL</c>. The rewriter runs only when BOTH this flag and
        /// <see cref="EnableFhirDateContainment"/> are enabled, because the End-only predicate is
        /// result-equivalent to the legacy overlap form only under containment semantics. Disabled by
        /// default; opt in per environment by setting
        /// <c>FhirSqlServer:EnableScalarTemporalEqualityRewriter=true</c> in configuration or
        /// the environment variable <c>FhirSqlServer__EnableScalarTemporalEqualityRewriter=true</c>.
        /// Tracked alongside AB#191826.
        /// </summary>
        public bool EnableScalarTemporalEqualityRewriter { get; set; } = false;

        /// <summary>
        /// When true, date/time equality (<c>eq</c>, and the equivalently-shaped <c>ap</c>) searches use the
        /// FHIR-spec containment semantics that Core already emits
        /// (<c>DateTimeStart &gt;= lo AND DateTimeEnd &lt;= hi</c>: the resource's stored period must sit
        /// inside the query window). The legacy
        /// <see cref="Microsoft.Health.Fhir.Core.Features.Search.Expressions.DateTimeEqualityRewriter"/>,
        /// which weakens equality to the non-spec overlap form, is bypassed. Because containment can never
        /// be satisfied by a stored period longer than the query window, no temporal <c>UNION ALL</c> is
        /// emitted in any query shape.
        ///
        /// This is a search-result behavior change: a finer-precision query (for example an exact day) no
        /// longer matches a coarser-precision stored value (for example a month or year) that the overlap
        /// form used to satisfy. <strong>Disabled by default</strong> so out-of-the-box behavior matches the
        /// legacy overlap semantics; opt in per environment by setting
        /// <c>FhirSqlServer:EnableFhirDateContainment=true</c> in configuration or the environment variable
        /// <c>FhirSqlServer__EnableFhirDateContainment=true</c>. Note: the temporal <c>UNION ALL</c> is
        /// removed regardless of this flag's value (the day-split union no longer exists); this flag only
        /// governs overlap-vs-containment result semantics.
        /// </summary>
        public bool EnableFhirDateContainment { get; set; } = false;
    }
}

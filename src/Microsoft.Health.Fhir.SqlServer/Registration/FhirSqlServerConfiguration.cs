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
        /// When true, <see cref="Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.ScalarTemporalEqualityRewriter"/>
        /// is included in the search-expression pipeline so allow-listed scalar date parameters
        /// (currently <c>birthdate</c>) get the End-only / UNION ALL optimization. Disabled by
        /// default — opt in per environment by setting
        /// <c>FhirSqlServer:EnableScalarTemporalEqualityRewriter=true</c> in configuration or
        /// the environment variable <c>FhirSqlServer__EnableScalarTemporalEqualityRewriter=true</c>.
        /// Tracked alongside AB#191826.
        /// </summary>
        public bool EnableScalarTemporalEqualityRewriter { get; set; } = false;
    }
}

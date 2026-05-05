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
    }
}

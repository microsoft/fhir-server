// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Encryption.Customer.Health;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Features.Health;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Health
{
    public class TestSqlHealthCheck : SqlHealthCheck
    {
        public TestSqlHealthCheck(
            ValueCache<CustomerKeyHealth> customerKeyHealthCache,
            ILogger<SqlHealthCheck> logger)
            : base(
                  customerKeyHealthCache,
                  logger)
        {
        }
    }
}

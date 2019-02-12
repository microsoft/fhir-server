// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Audit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    public class AuditTestFixture : HttpIntegrationTestFixture<StartupWithTraceAuditLogger>
    {
        private TraceAuditLogger _auditLogger;

        public TraceAuditLogger AuditLogger
        {
            get
            {
                if (_auditLogger == null)
                {
                    _auditLogger = (TraceAuditLogger)Server?.Host.Services.GetRequiredService<IAuditLogger>();
                }

                return _auditLogger;
            }
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    public class AuditTestFixture : HttpIntegrationTestFixture<StartupWithTraceAuditLogger>
    {
        private TraceAuditLogger _auditLogger;

        public AuditTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        public TraceAuditLogger AuditLogger
        {
            get => _auditLogger ?? (_auditLogger = (TraceAuditLogger)(TestFhirServer as InProcTestFhirServer)?.Server.Host.Services.GetRequiredService<IAuditLogger>());
        }
    }
}

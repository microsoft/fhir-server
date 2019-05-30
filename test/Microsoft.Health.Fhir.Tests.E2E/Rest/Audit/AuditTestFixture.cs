// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    public class AuditTestFixture : HttpIntegrationTestFixture
    {
        private TraceAuditLogger _auditLogger;

        public AuditTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
        }

        public TraceAuditLogger AuditLogger
        {
            get
            {
                if (_auditLogger == null)
                {
                    _auditLogger = Server?.Host.Services.GetRequiredService<TraceAuditLogger>();
                }

                return _auditLogger;
            }
        }

        internal override Action<IServiceCollection> ConfigureServices => (services) =>
        {
            services.Add<TraceAuditLogger>()
                .Singleton()
                .AsSelf()
                .AsService<IAuditLogger>();
        };
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class ExceptionTestFixture : HttpIntegrationTestFixture
    {
        public ExceptionTestFixture(DataStore dataStore, Format format, FhirVersion fhirVersion)
            : base(dataStore, format, fhirVersion)
        {
        }

        internal override Action<IServiceCollection> ConfigureTestServices => (services) =>
        {
            services.Add<ExceptionMiddleware>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();
        };
    }
}

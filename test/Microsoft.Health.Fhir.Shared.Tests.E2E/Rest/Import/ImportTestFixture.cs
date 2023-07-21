// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTestFixture<T> : HttpIntegrationTestFixture<T>
    {
        private MetricHandler _metricHandler;

        public ImportTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            StorageAccount = new ImportTestStorageAccount();
        }

        public MetricHandler MetricHandler
        {
            get => _metricHandler ?? (_metricHandler = (MetricHandler)(TestFhirServer as InProcTestFhirServer)?.Server.Host.Services.GetRequiredService<INotificationHandler<ImportJobMetricsNotification>>());
        }

        public ImportTestStorageAccount StorageAccount { get; private set; }
    }
}

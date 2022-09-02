// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using MediatR;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportRebuildIndexesTestFixture<T> : HttpIntegrationTestFixture<T>
    {
        private const string LocalIntegrationStoreConnectionString = "UseDevelopmentStorage=true";
        private MetricHandler _metricHandler;

        public ImportRebuildIndexesTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            CloudStorageAccount storageAccount;

            string integrationStoreFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestIntegrationStoreUri");
            string integrationStoreKeyFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestIntegrationStoreKey");
            if (!string.IsNullOrEmpty(integrationStoreFromEnvironmentVariable) && !string.IsNullOrEmpty(integrationStoreKeyFromEnvironmentVariable))
            {
                Uri integrationStoreUri = new Uri(integrationStoreFromEnvironmentVariable);
                string storageAccountName = integrationStoreUri.Host.Split('.')[0];
                StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, integrationStoreKeyFromEnvironmentVariable);
                storageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
            }
            else
            {
                CloudStorageAccount.TryParse(LocalIntegrationStoreConnectionString, out storageAccount);
            }

            if (storageAccount == null)
            {
                throw new Exception(string.Format("Unable to create a cloud storage account. {0}", integrationStoreFromEnvironmentVariable ?? string.Empty));
            }

            CloudStorageAccount = storageAccount;
        }

        public MetricHandler MetricHandler
        {
            get => _metricHandler ?? (_metricHandler = (MetricHandler)(TestFhirServer as InProcTestFhirServer)?.Server.Host.Services.GetRequiredService<INotificationHandler<ImportJobMetricsNotification>>());
        }

        public CloudStorageAccount CloudStorageAccount { get; private set; }

        public string IntegrationStoreConnectionString { get; private set; }
    }
}

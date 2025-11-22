// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.JobManagement;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Reindex
{
    /// <summary>
    /// Extended HTTP integration test fixture that overrides the reindex delay multiplier for faster E2E tests.
    /// Uses the minimum sync interval between instances to optimize test execution time while maintaining
    /// consistency with production behavior.
    /// </summary>
    public class ReindexTestFixture : HttpIntegrationTestFixture
    {
        public ReindexTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
        }

        /// <summary>
        /// Override configuration settings for faster reindex testing.
        /// Sets the ReindexDelayMultiplier to 1 (minimum) to reduce delay while maintaining
        /// the SearchParameterCacheRefreshIntervalSeconds for consistency with production behavior.
        /// Also initializes the test fixture by cleaning up test data resources.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            // Override the reindex delay configuration if the server is in-process
            if (IsUsingInProcTestServer && TestFhirServer is InProcTestFhirServer inProcServer)
            {
                var serviceProvider = inProcServer.Server.Services;

                try
                {
                    // Update OperationsConfiguration for ReindexDelayMultiplier
                    // Setting to 1 uses the minimum delay while maintaining the sync interval
                    var operationsOptions = serviceProvider.GetRequiredService<IOptions<OperationsConfiguration>>();
                    var coreConfigurations = serviceProvider.GetRequiredService<IOptions<Core.Configs.CoreFeatureConfiguration>>();
                    if (operationsOptions?.Value?.Reindex != null)
                    {
                        operationsOptions.Value.Reindex.ReindexDelayMultiplier = 1;
                        coreConfigurations.Value.SearchParameterCacheRefreshIntervalSeconds = 2;
                    }

                    // Override job hosting polling frequency for faster dequeuing
                    var jobHosting = serviceProvider.GetService<JobHosting>();
                    if (jobHosting != null)
                    {
                        jobHosting.PollingFrequencyInSeconds = 2;
                    }
                }
                catch (Exception ex)
                {
                    var logger = serviceProvider.GetService<ILogger<ReindexTestFixture>>();
                    logger?.LogWarning(ex, "Failed to configure reindex delay multiplier, using default values");

                    // Continue with default configuration if override fails
                }
            }

            // Clean up test data resources to ensure we start with clean state
            await TestFhirClient.DeleteAllResources(ResourceType.Specimen, null);
            await TestFhirClient.DeleteAllResources(ResourceType.Immunization, null);
            await TestFhirClient.DeleteAllResources(ResourceType.Person, null);
        }
    }
}

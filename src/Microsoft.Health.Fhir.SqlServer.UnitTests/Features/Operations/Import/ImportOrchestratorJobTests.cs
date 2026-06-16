// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportOrchestratorJobTests
    {
        [InlineData(ImportMode.InitialLoad)]
        [InlineData(ImportMode.IncrementalLoad)]
        [Theory]
        public async Task GivenAnOrchestratorJobAndWrongEtag_WhenOrchestratorJobStart_ThenJobShouldFailWithDetails(ImportMode importMode)
        {
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorJobDefinition importOrchestratorInputData = new ImportOrchestratorJobDefinition();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();

            IMediator mediator = Substitute.For<IMediator>();

            importOrchestratorInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");
            importOrchestratorInputData.ImportMode = importMode;

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                contextAccessor,
                integrationDataStoreClient,
                testQueueClient,
                Options.Create(new ImportJobConfiguration()),
                loggerFactory,
                auditLogger);

            JobExecutionException jobExecutionException = await Assert.ThrowsAsync<JobExecutionException>(async () => await orchestratorJob.ExecuteAsync(orchestratorJobInfo, CancellationToken.None));
            ImportJobErrorResult resultDetails = (ImportJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreateTime == orchestratorJobInfo.CreateDate &&
                    notification.DataSize == 0 &&
                    notification.SucceededCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());

            if (importMode == ImportMode.IncrementalLoad)
            {
                var incrementalImportProperties = new Dictionary<string, string>();
                incrementalImportProperties["JobId"] = orchestratorJobInfo.Id.ToString();
                incrementalImportProperties["SucceededResources"] = "0";
                incrementalImportProperties["FailedResources"] = "0";

                auditLogger.Received(1);
                auditLogger.Received().LogAudit(
                    auditAction: Arg.Any<AuditAction>(),
                    operation: Arg.Any<string>(),
                    resourceType: Arg.Any<string>(),
                    requestUri: Arg.Any<Uri>(),
                    statusCode: Arg.Any<HttpStatusCode?>(),
                    correlationId: Arg.Any<string>(),
                    callerIpAddress: Arg.Any<string>(),
                    callerClaims: Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                    customHeaders: Arg.Any<IReadOnlyDictionary<string, string>>(),
                    operationType: Arg.Any<string>(),
                    callerAgent: Arg.Any<string>(),
                    additionalProperties: Arg.Is<IReadOnlyDictionary<string, string>>(dict =>
                                        dict.ContainsKey("JobId") && dict["JobId"].Equals(orchestratorJobInfo.Id.ToString()) &&
                                        dict.ContainsKey("SucceededResources") && dict["SucceededResources"].Equals("0") &&
                                        dict.ContainsKey("FailedResources") && dict["FailedResources"].Equals("0")));
            }
            else if (importMode == ImportMode.InitialLoad)
            {
                auditLogger.DidNotReceiveWithAnyArgs().LogAudit(
                               auditAction: default,
                               operation: default,
                               resourceType: default,
                               requestUri: default,
                               statusCode: default,
                               correlationId: default,
                               callerIpAddress: default,
                               callerClaims: default);
            }
        }

        [InlineData(ImportMode.InitialLoad)]
        [InlineData(ImportMode.IncrementalLoad)]
        [Theory]
        public async Task GivenAnOrchestratorJob_WhenIntegrationExceptionThrown_ThenJobShouldFailWithDetails(ImportMode importMode)
        {
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorJobDefinition importOrchestratorJobInputData = new ImportOrchestratorJobDefinition();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();

            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");
            importOrchestratorJobInputData.ImportMode = importMode;

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns<Task<Dictionary<string, object>>>(_ =>
                {
                    throw new IntegrationDataStoreException("dummy", HttpStatusCode.Unauthorized);
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                contextAccessor,
                integrationDataStoreClient,
                testQueueClient,
                Options.Create(new ImportJobConfiguration()),
                loggerFactory,
                auditLogger);

            JobExecutionException jobExecutionException = await Assert.ThrowsAsync<JobExecutionException>(async () => await orchestratorJob.ExecuteAsync(orchestratorJobInfo, CancellationToken.None));
            ImportJobErrorResult resultDetails = (ImportJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.Unauthorized, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreateTime == orchestratorJobInfo.CreateDate &&
                    notification.DataSize == 0 &&
                    notification.SucceededCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());

            if (importMode == ImportMode.IncrementalLoad)
            {
                var incrementalImportProperties = new Dictionary<string, string>();
                incrementalImportProperties["JobId"] = orchestratorJobInfo.Id.ToString();
                incrementalImportProperties["SucceededResources"] = "0";
                incrementalImportProperties["FailedResources"] = "0";

                auditLogger.Received(1);
                auditLogger.Received().LogAudit(
                    auditAction: Arg.Any<AuditAction>(),
                    operation: Arg.Any<string>(),
                    resourceType: Arg.Any<string>(),
                    requestUri: Arg.Any<Uri>(),
                    statusCode: Arg.Any<HttpStatusCode?>(),
                    correlationId: Arg.Any<string>(),
                    callerIpAddress: Arg.Any<string>(),
                    callerClaims: Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                    customHeaders: Arg.Any<IReadOnlyDictionary<string, string>>(),
                    operationType: Arg.Any<string>(),
                    callerAgent: Arg.Any<string>(),
                    additionalProperties: Arg.Is<IReadOnlyDictionary<string, string>>(dict =>
                                        dict.ContainsKey("JobId") && dict["JobId"].Equals(orchestratorJobInfo.Id.ToString()) &&
                                        dict.ContainsKey("SucceededResources") && dict["SucceededResources"].Equals("0") &&
                                        dict.ContainsKey("FailedResources") && dict["FailedResources"].Equals("0")));
            }
            else if (importMode == ImportMode.InitialLoad)
            {
                auditLogger.DidNotReceiveWithAnyArgs().LogAudit(
                               auditAction: default,
                               operation: default,
                               resourceType: default,
                               requestUri: default,
                               statusCode: default,
                               correlationId: default,
                               callerIpAddress: default,
                               callerClaims: default);
            }
        }
    }
}

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
            Assert.True(jobExecutionException.IsCustomerCaused);

            _ = mediator.Received().PublishAsync(
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

        [Theory]
        [InlineData(null)]
        [InlineData("relative-blob")]
        public async Task GivenMissingOrInvalidBlobUri_WhenOrchestratorExecutes_ThenFailureIsCustomerCaused(string blobUri)
        {
            // Arrange
            var input = CreateInput();
            input.Input[0].Url = blobUri == null ? null : new Uri(blobUri, UriKind.Relative);
            var job = CreateOrchestratorJob(Substitute.For<IIntegrationDataStoreClient>(), Substitute.For<IQueueClient>());
            var jobInfo = new JobInfo { Definition = JsonConvert.SerializeObject(input) };

            // Act
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => job.ExecuteAsync(jobInfo, CancellationToken.None));

            // Assert
            Assert.True(exception.IsCustomerCaused);
            Assert.Equal(HttpStatusCode.BadRequest, Assert.IsType<ImportJobErrorResult>(exception.Error).HttpStatusCode);
        }

        [Theory]
        [InlineData("before")]
        [InlineData("validation")]
        [InlineData("enqueue")]
        public async Task GivenCancellation_WhenOrchestratorExecutes_ThenCancellationIsPropagated(string cancellationStage)
        {
            // Arrange
            using var cancellationSource = new CancellationTokenSource();
            var canceled = new OperationCanceledException(cancellationSource.Token);
            var client = Substitute.For<IIntegrationDataStoreClient>();
            client.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                    {
                        if (cancellationStage == "validation")
                        {
                            cancellationSource.Cancel();
                        }

                        return cancellationStage == "validation"
                            ? Task.FromException<Dictionary<string, object>>(canceled)
                            : Task.FromResult(new Dictionary<string, object> { [IntegrationDataStoreClientConstants.BlobPropertyLength] = 1L });
                    });
            var queue = Substitute.For<IQueueClient>();
            queue.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<Task<IReadOnlyList<JobInfo>>>(_ =>
                    {
                        cancellationSource.Cancel();
                        throw canceled;
                    });
            var job = CreateOrchestratorJob(client, queue);
            var jobInfo = new JobInfo { Definition = JsonConvert.SerializeObject(CreateInput()) };
            if (cancellationStage == "before")
            {
                cancellationSource.Cancel();
            }

            // Act
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => job.ExecuteAsync(jobInfo, cancellationSource.Token));

            // Assert
            Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenJobExecutionException_WhenOrchestratorExecutes_ThenFailureClassificationIsPreserved(bool isCustomerCaused)
        {
            // Arrange
            var client = Substitute.For<IIntegrationDataStoreClient>();
            var error = new ImportJobErrorResult { ErrorMessage = "Input failure.", HttpStatusCode = HttpStatusCode.BadRequest };
            client.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns<Task<Dictionary<string, object>>>(_ => throw new JobExecutionException(error.ErrorMessage, error, isCustomerCaused));
            var job = CreateOrchestratorJob(client, Substitute.For<IQueueClient>());
            var jobInfo = new JobInfo { Definition = JsonConvert.SerializeObject(CreateInput()) };

            // Act
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => job.ExecuteAsync(jobInfo, CancellationToken.None));

            // Assert
            Assert.Equal(isCustomerCaused, exception.IsCustomerCaused);
            Assert.Same(error, exception.Error);
        }

        private static ImportOrchestratorJobDefinition CreateInput()
        {
            return new ImportOrchestratorJobDefinition
            {
                BaseUri = new Uri("http://dummy"),
                RequestUri = new Uri("http://dummy"),
                Input = new List<InputResource> { new InputResource { Type = "Patient", Url = new Uri("http://dummy") } },
            };
        }

        private static ImportOrchestratorJob CreateOrchestratorJob(IIntegrationDataStoreClient client, IQueueClient queue)
        {
            return new ImportOrchestratorJob(
                Substitute.For<IMediator>(),
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>(),
                client,
                queue,
                Options.Create(new ImportJobConfiguration()),
                NullLoggerFactory.Instance,
                Substitute.For<IAuditLogger>());
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

            _ = mediator.Received().PublishAsync(
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

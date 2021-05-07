// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class ImportOrchestratorTaskTests
    {
        [Fact]
        public async Task GivenValidImportRequest_WhenOrchestratorTaskStart_ThenTaskShouldBeCompleted()
        {
            IFhirDataBulkImportOperation fhirDataBulkImportOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            ImportOrchestratorTaskInputData importOrchestratorTaskInputData = new ImportOrchestratorTaskInputData();
            ImportOrchestratorTaskContext importOrchestratorTaskContext = new ImportOrchestratorTaskContext();
            TestTaskManager taskManager = new TestTaskManager(t =>
                {
                    if (t == null)
                    {
                        return null;
                    }

                    ImportProcessingTaskResult processingResult = new ImportProcessingTaskResult();
                    processingResult.ResourceType = "Resource";
                    processingResult.SucceedCount = 1;
                    processingResult.FailedCount = 1;
                    processingResult.ErrorLogLocation = "http://dummy/error";

                    t.Result = JsonConvert.SerializeObject(new TaskResultData(TaskResult.Success, JsonConvert.SerializeObject(processingResult)));
                    t.Status = TaskManagement.TaskStatus.Completed;
                    return t;
                });

            importOrchestratorTaskInputData.TaskId = Guid.NewGuid().ToString("N");
            importOrchestratorTaskInputData.TaskCreateTime = Clock.UtcNow;
            importOrchestratorTaskInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy") });
            importOrchestratorTaskInputData.Input = inputs;
            importOrchestratorTaskInputData.InputFormat = "ndjson";
            importOrchestratorTaskInputData.InputSource = new Uri("http://dummy");
            importOrchestratorTaskInputData.MaxConcurrentProcessingTaskCount = 1;
            importOrchestratorTaskInputData.MaxConcurrentRebuildIndexOperationCount = 3;
            importOrchestratorTaskInputData.ProcessingTaskQueueId = "default";
            importOrchestratorTaskInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns(_ => 0L);

            ImportOrchestratorTask orchestratorTask = new ImportOrchestratorTask(
                importOrchestratorTaskInputData,
                importOrchestratorTaskContext,
                taskManager,
                sequenceIdGenerator,
                contextUpdater,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                loggerFactory);

            TaskResultData result = await orchestratorTask.ExecuteAsync();
            ImportTaskResult resultDetails = JsonConvert.DeserializeObject<ImportTaskResult>(result.ResultData);
            Assert.Equal(TaskResult.Success, result.Result);
            Assert.Equal(1, resultDetails.Output.Count);
            Assert.Equal(1, resultDetails.Output.First().Count = 1);
            Assert.NotNull(resultDetails.Output.First().InputUrl);
            Assert.NotEmpty(resultDetails.Output.First().Type);
            Assert.Equal(1, resultDetails.Error.Count);
            Assert.Equal(1, resultDetails.Error.First().Count = 1);
            Assert.NotNull(resultDetails.Error.First().InputUrl);
            Assert.NotEmpty(resultDetails.Error.First().Type);
            Assert.NotNull(resultDetails.Error.First().Url);
            Assert.NotEmpty(resultDetails.Request);
            Assert.Equal(importOrchestratorTaskInputData.TaskCreateTime, resultDetails.TransactionTime);
        }
    }
}

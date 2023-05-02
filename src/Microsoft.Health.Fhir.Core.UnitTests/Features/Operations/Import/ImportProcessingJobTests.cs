// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportProcessingJobTests
    {
        [Fact]
        public async Task GivenImportInput_WhenStartFromClean_ThenAllResoruceShouldBeImported()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();
            await VerifyCommonImportAsync(inputData, result);
        }

        [Fact]
        public async Task GivenImportInput_WhenExceptionThrowForLoad_ThenRetriableExceptionShouldBeThrow()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            throw new InvalidOperationException();
                        }
                        finally
                        {
                            resourceChannel.Writer.Complete();
                        }
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    return new ImportProcessingProgress();
                });

            Progress<string> progress = new Progress<string>();
            ImportProcessingJob job = new ImportProcessingJob(
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableJobException>(() => job.ExecuteAsync(GetJobInfo(inputData, result), progress, CancellationToken.None));
        }

        [Fact]
        public async Task GivenImportInput_WhenOperationWasCancelledExceptionThrow_ThenJobShouldFailed()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    if (callInfo[2] != null) // always true
                    {
                        throw new OperationCanceledException();
                    }

                    return new ImportProcessingProgress();
                });

            ImportProcessingJob job = new ImportProcessingJob(
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<JobExecutionException>(() => job.ExecuteAsync(GetJobInfo(inputData, result), new Progress<string>(), CancellationToken.None));
        }

        private static async Task VerifyCommonImportAsync(ImportProcessingJobDefinition inputData, ImportProcessingJobResult currentResult)
        {
            long succeedCountFromProgress = currentResult.SucceededResources;
            long failedCountFromProgress = currentResult.FailedResources;

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        var resourceWrapper = new ResourceWrapper(
                            Guid.NewGuid().ToString(),
                            "0",
                            "Patient",
                            new RawResource(Guid.NewGuid().ToString(), Fhir.Core.Models.FhirResourceFormat.Json, true),
                            new ResourceRequest("POST"),
                            DateTimeOffset.UtcNow,
                            false,
                            null,
                            null,
                            null,
                            "SearchParam");

                        await resourceChannel.Writer.WriteAsync(new ImportResource(0, 0, 0, resourceWrapper));
                        await resourceChannel.Writer.WriteAsync(new ImportResource(1, 0, "Error"));
                        resourceChannel.Writer.Complete();
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    Channel<ImportResource> resourceChannel = (Channel<ImportResource>)callInfo[0];
                    var progress = new ImportProcessingProgress();
                    await foreach (var resource in resourceChannel.Reader.ReadAllAsync())
                    {
                        if (string.IsNullOrEmpty(resource.ImportError))
                        {
                            progress.SucceededResources++;
                        }
                        else
                        {
                            progress.FailedResources++;
                        }
                    }

                    return progress;
                });

            string progressResult = null;
            var progress = new Progress<string>((r) => { progressResult = r; });
            var job = new ImportProcessingJob(loader, importer, importErrorStoreFactory, contextAccessor, loggerFactory);

            string resultString = await job.ExecuteAsync(GetJobInfo(inputData, currentResult), progress, CancellationToken.None);
            ImportProcessingJobResult result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(resultString);
            Assert.Equal(1 + failedCountFromProgress, result.FailedResources);
            Assert.Equal(1 + succeedCountFromProgress, result.SucceededResources);
        }

        private ImportProcessingJobDefinition GetInputData()
        {
            var inputData = new ImportProcessingJobDefinition();
            inputData.BaseUriString = "http://dummy";
            inputData.ResourceLocation = "http://dummy";
            inputData.ResourceType = "Patient";
            inputData.JobId = Guid.NewGuid().ToString("N");
            inputData.UriString = "http://dummy";

            return inputData;
        }

        private static JobInfo GetJobInfo(ImportProcessingJobDefinition data, ImportProcessingJobResult result)
        {
            var jobInfo = new JobInfo
            {
                Definition = JsonConvert.SerializeObject(data),
                Result = result != null ? JsonConvert.SerializeObject(result) : null,
            };

            return jobInfo;
        }
    }
}

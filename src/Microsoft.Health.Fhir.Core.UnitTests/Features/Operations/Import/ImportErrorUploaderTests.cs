// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class ImportErrorUploaderTests
    {
        private const string ErrorMessage = "ErrorMessage";

        [Fact]
        public async Task GivenListOfProcessErrors_WhenUpload_AllErrorsShouldBeUploaded()
        {
            await VerifyImportErrorUploader(0);
        }

        [Fact]
        public async Task GivenListOfProcessErrors_WhenUploadFromMiddle_AllErrorsShouldBeUploaded()
        {
            await VerifyImportErrorUploader(1);
        }

        private static async Task VerifyImportErrorUploader(int startBatchId)
        {
            Dictionary<long, string[]> result = new Dictionary<long, string[]>();
            long[] commitedPartIds = new long[0];

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.PrepareResourceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    string container = (string)callInfo[0];
                    string file = (string)callInfo[1];

                    return new Uri($"http://{container}/{file}");
                });

            integrationDataStoreClient.UploadPartDataAsync(Arg.Any<Uri>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    Stream dataStream = (Stream)callInfo[1];
                    long partId = (long)callInfo[2];
                    using StreamReader reader = new StreamReader(dataStream);

                    List<string> errors = new List<string>();
                    string content = null;
                    while ((content = reader.ReadLine()) != null)
                    {
                        errors.Add(content);
                    }

                    result[partId] = errors.ToArray();

                    return Task.CompletedTask;
                });

            integrationDataStoreClient.CommitDataAsync(Arg.Any<Uri>(), Arg.Any<long[]>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    long[] partIds = (long[])callInfo[1];
                    commitedPartIds = partIds;

                    return Task.CompletedTask;
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();
            serializer.Serialize(Arg.Any<ProcessError>()).ReturnsForAnyArgs(callInfo =>
            {
                ProcessError error = (ProcessError)callInfo[0];
                return $"{error.LineNumber}:{error.ErrorMessage}";
            });
            ImportErrorUploader uploader = new ImportErrorUploader(integrationDataStoreClient, serializer, NullLogger<ImportErrorUploader>.Instance);
            uploader.MaxBatchSize = 2;

            Channel<BatchProcessErrorRecord> errorsChannel = Channel.CreateUnbounded<BatchProcessErrorRecord>();
            await errorsChannel.Writer.WriteAsync(new BatchProcessErrorRecord(new List<ProcessError>() { new ProcessError(0, ErrorMessage), new ProcessError(1, ErrorMessage) }, 3));
            await errorsChannel.Writer.WriteAsync(new BatchProcessErrorRecord(new List<ProcessError>(), 10));
            await errorsChannel.Writer.WriteAsync(new BatchProcessErrorRecord(new List<ProcessError>() { new ProcessError(2, ErrorMessage), new ProcessError(3, ErrorMessage), new ProcessError(4, ErrorMessage) }, 20));
            errorsChannel.Writer.Complete();
            await uploader.HandleImportErrorAsync("test", errorsChannel, startBatchId, (batchId, surrogatedId) => { }, CancellationToken.None);
            Assert.Equal(startBatchId + 2, commitedPartIds.Length);
            for (int i = 0; i < startBatchId + 2; ++i)
            {
                Assert.Equal(i, commitedPartIds[i]);
            }

            Assert.Equal(2, result[startBatchId].Length);
            Assert.Equal(3, result[startBatchId + 1].Length);
            Assert.Equal(2, result.Count);
        }
    }
}

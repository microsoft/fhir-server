// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        [Fact]
        public async Task GivenListOfProcessErrorsLargeThanBatchCount_WhenWriteToStore_AllErrorsShouldBeUploaded()
        {
            await VerifyImportErrorUploader(101, 4321);
        }

        [Fact]
        public async Task GivenListOfProcessErrorsEqualsBatchCount_WhenWriteToStore_AllErrorsShouldBeUploaded()
        {
            await VerifyImportErrorUploader(101, 101);
        }

        [Fact]
        public async Task GivenListOfProcessErrorsLessThanBatchCount_WhenWriteToStore_AllErrorsShouldBeUploaded()
        {
            await VerifyImportErrorUploader(101, 7);
        }

        [Fact]
        public async Task GivenNoProcessError_WhenWriteToStore_NoErrorShouldBeUploaded()
        {
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();

            ImportErrorsManager uploader = new ImportErrorsManager(integrationDataStoreClient, serializer, NullLogger<ImportErrorsManager>.Instance);
            Assert.Equal(0, await uploader.WriteErrorsAsync(new Uri("http://dummy"), null, CancellationToken.None));
        }

        private static async Task VerifyImportErrorUploader(int batchCount, int errorCount)
        {
            List<string> result = new List<string>();
            List<string> commitedPartIds = new List<string>();

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.PrepareResourceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    string container = (string)callInfo[0];
                    string file = (string)callInfo[1];

                    return new Uri($"http://{container}/{file}");
                });

            integrationDataStoreClient.UploadBlockAsync(Arg.Any<Uri>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    Stream dataStream = (Stream)callInfo[1];
                    string partId = (string)callInfo[2];
                    StreamReader reader = new StreamReader(dataStream);

                    List<string> errors = new List<string>();
                    string content = null;
                    while ((content = reader.ReadLine()) != null)
                    {
                        errors.Add(content);
                    }

                    result.AddRange(errors.ToArray());

                    return Task.CompletedTask;
                });

            integrationDataStoreClient.AppendCommitAsync(Arg.Any<Uri>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    string[] blockIds = (string[])callInfo[1];
                    commitedPartIds.AddRange(blockIds);

                    return Task.CompletedTask;
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();
            serializer.Serialize(Arg.Any<ImportResourceParseError>()).ReturnsForAnyArgs(callInfo =>
            {
                ImportResourceParseError error = (ImportResourceParseError)callInfo[0];
                return $"{error.LineNumber}:{error.ErrorMessage}";
            });
            ImportErrorsManager uploader = new ImportErrorsManager(integrationDataStoreClient, serializer, NullLogger<ImportErrorsManager>.Instance);
            uploader.MaxBatchSize = batchCount;

            for (int i = 0; i < errorCount; ++i)
            {
                uploader.Add(new ImportResourceParseError(i, i, i.ToString()));
            }

            await uploader.WriteErrorsAsync(new Uri("http://dummy"), errorCount - 2, CancellationToken.None);
            await uploader.WriteErrorsAsync(new Uri("http://dummy"), null, CancellationToken.None);
            Assert.Equal(((errorCount - 1) / batchCount) + 2, commitedPartIds.Count);
            Assert.Equal(errorCount, result.Count);
        }
    }
}

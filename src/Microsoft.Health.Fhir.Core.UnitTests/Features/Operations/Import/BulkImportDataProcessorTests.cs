// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class BulkImportDataProcessorTests
    {
        private const string ErrorMessage = "Excepted Error.";

        [Fact]
        public async Task GivenBatchRawResources_WhenProcessingData_ValidDataShouldBeOutputInOrder()
        {
            await VerifyProcessorBehaviourAsync(12345, 79);
        }

        [Fact]
        public async Task GivenBatchRawResourcesEqualBatchSize_WhenProcessingData_ValidDataShouldBeOutputInOrder()
        {
            await VerifyProcessorBehaviourAsync(79, 79);
        }

        [Fact]
        public async Task GivenBatchRawResourcesLessThanBatchSize_WhenProcessingData_ValidDataShouldBeOutputInOrder()
        {
            await VerifyProcessorBehaviourAsync(5, 79);
        }

        private static async Task VerifyProcessorBehaviourAsync(int inputResourceCount, int maxBatchSize)
        {
            long startSurrogatedId = 1005;

            IBulkImportDataExtractor bulkImportDataExtractor = Substitute.For<IBulkImportDataExtractor>();
            bulkImportDataExtractor.GetBulkImportResourceWrapper(Arg.Any<string>())
                .Returns<BulkImportResourceWrapper>(callInfo =>
                {
                    string content = (string)callInfo[0];
                    BulkImportResourceWrapper resourceWrapper = ParseResourceResult(content, maxBatchSize);

                    if (resourceWrapper == null)
                    {
                        throw new InvalidOperationException(ErrorMessage);
                    }

                    return resourceWrapper;
                });
            BulkRawResourceProcessor processor = new BulkRawResourceProcessor(bulkImportDataExtractor, NullLogger<BulkRawResourceProcessor>.Instance);
            processor.MaxBatchSize = maxBatchSize;

            Channel<string> inputs = Channel.CreateUnbounded<string>();
            Channel<BatchProcessErrorRecord> errorsChannel = Channel.CreateUnbounded<BatchProcessErrorRecord>();
            Channel<BulkImportResourceWrapper> outputs = Channel.CreateUnbounded<BulkImportResourceWrapper>();

            List<string> inputStrings = new List<string>();
            Task produceTask = Task.Run(async () =>
            {
                for (int i = 0; i < inputResourceCount; ++i)
                {
                    string content = (i + startSurrogatedId).ToString();
                    inputStrings.Add(content);
                    await inputs.Writer.WriteAsync(content);
                }

                inputs.Writer.Complete();
            });

            await processor.ProcessingDataAsync(inputs, outputs, errorsChannel, startSurrogatedId, CancellationToken.None);
            await produceTask;

            int currentIndex = 0;
            List<long> failedLineNumbers = new List<long>();
            await foreach (BulkImportResourceWrapper resourceWrapper in outputs.Reader.ReadAllAsync())
            {
                BulkImportResourceWrapper expectedResource = null;
                while ((expectedResource = ParseResourceResult(inputStrings[currentIndex++], maxBatchSize)) == null)
                {
                    failedLineNumbers.Add(currentIndex - 1);
                }

                Assert.Equal(expectedResource.CompressedRawData, resourceWrapper.CompressedRawData);
            }

            Assert.Equal(inputResourceCount, currentIndex);

            long endSurrogatedId = 0;
            await foreach (BatchProcessErrorRecord errorRecord in errorsChannel.Reader.ReadAllAsync())
            {
                foreach (ProcessError error in errorRecord.ProcessErrors)
                {
                    Assert.Equal(failedLineNumbers.First(), error.LineNumber);
                    failedLineNumbers.RemoveAt(0);
                }

                endSurrogatedId = errorRecord.LastSurragatedId;
            }

            Assert.Equal(startSurrogatedId + inputResourceCount, endSurrogatedId);
            Assert.Empty(failedLineNumbers);
        }

        private static BulkImportResourceWrapper ParseResourceResult(string content, int maxBatchSize)
        {
            long id = long.Parse(content);
            if (id % maxBatchSize == 1)
            {
                return null;
            }

            return new BulkImportResourceWrapper(null, Encoding.UTF8.GetBytes(content));
        }
    }
}

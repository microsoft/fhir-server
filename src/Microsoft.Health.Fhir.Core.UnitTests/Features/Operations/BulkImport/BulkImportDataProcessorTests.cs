// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class BulkImportDataProcessorTests
    {
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
            IBulkImportDataExtractor bulkImportDataExtractor = Substitute.For<IBulkImportDataExtractor>();
            bulkImportDataExtractor.GetBulkImportResourceWrapper(Arg.Any<string>())
                .Returns<BulkImportResourceWrapper>(callInfo =>
                {
                    string content = (string)callInfo[0];
                    return new BulkImportResourceWrapper(null, Encoding.UTF8.GetBytes(content));
                });
            BulkRawResourceProcessor processor = new BulkRawResourceProcessor(bulkImportDataExtractor);
            processor.MaxBatchSize = maxBatchSize;

            Channel<string> inputs = Channel.CreateUnbounded<string>();
            Channel<BulkImportResourceWrapper> outputs = Channel.CreateUnbounded<BulkImportResourceWrapper>();
            long startSurrogatedId = 1005;

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

            await processor.ProcessingDataAsync(inputs, outputs, startSurrogatedId, CancellationToken.None);
            await produceTask;

            int currentIndex = 0;
            await foreach (BulkImportResourceWrapper resourceWrapper in outputs.Reader.ReadAllAsync())
            {
                string idString = Encoding.UTF8.GetString(resourceWrapper.CompressedRawData);
                Assert.Equal(inputStrings[currentIndex++], idString);
            }

            Assert.Equal(inputResourceCount, currentIndex);
        }
    }
}

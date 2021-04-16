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
    public class BulkResourceLoaderTests
    {
        [Fact]
        public async Task GivenBulkResourceLoader_WhenLoadingData_DataShouldBeReturnedInOrder()
        {
            int count = 10005;
            int skipLine = 47;

            List<string> inputStrings = new List<string>();
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            for (int i = 0; i < count; ++i)
            {
                string content = Guid.NewGuid().ToString("N");
                inputStrings.Add(content);
                await writer.WriteLineAsync(content);
            }

            await writer.FlushAsync();
            stream.Position = 0;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            BulkResourceLoader loader = new BulkResourceLoader(integrationDataStoreClient, NullLogger<BulkResourceLoader>.Instance);
            Channel<string> output = Channel.CreateUnbounded<string>();

            await loader.LoadToChannelAsync(output, null, skipLine, CancellationToken.None);

            int currentIndex = skipLine;
            await foreach (string content in output.Reader.ReadAllAsync())
            {
                Assert.Equal(inputStrings[currentIndex++], content);
            }

            Assert.Equal(count, currentIndex);
        }
    }
}

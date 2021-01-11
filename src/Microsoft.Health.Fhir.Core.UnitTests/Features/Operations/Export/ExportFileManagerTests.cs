// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportFileManagerTests
    {
        private ExportFileManager _exportFileManager;
        private ExportJobRecord _exportJobRecord;
        private IExportDestinationClient _exportDestinationClient;
        private CancellationToken _cancellationTokenNone = CancellationToken.None;

        public ExportFileManagerTests()
        {
            _exportDestinationClient = Substitute.For<IExportDestinationClient>();
            _exportDestinationClient
                .CreateFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Uri>(callInfo => new Uri("https://localhost/" + callInfo.ArgAt<string>(0)));

            string exportJobConfigurationFormat = $"{ExportFormatTags.ResourceName}";
            _exportJobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                exportJobConfigurationFormat,
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1);

            _exportFileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);
        }

        [Fact]
        public async Task GivenMultipleFilesInOrderForResourceTypeInOutput_WhenWriteToFile_ThenWritesToLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);
            ExportFileInfo file3 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-3.ndjson"), sequence: 3);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file1, file2, file3 });

            await _exportFileManager.WriteToFile(resourceType, "partId", new byte[] { 1 }, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file3.FileUri), Arg.Is(_cancellationTokenNone));
        }

        [Fact]
        public async Task GivenMultipleFilesOutOfOrderForResourceTypeInOutput_WhenWriteToFile_ThenWritesToLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);
            ExportFileInfo file3 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-3.ndjson"), sequence: 3);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file2, file3, file1 });

            await _exportFileManager.WriteToFile(resourceType, "partId", new byte[] { 1 }, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file3.FileUri), Arg.Is(_cancellationTokenNone));
        }

        [Fact]
        public async Task GivenMultipleFilesForMultipleResourceTypeInOutput_WhenWriteToFile_ThenWritesToCorrectLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);

            string resourceType2 = "Observation";
            ExportFileInfo file3 = new ExportFileInfo(resourceType2, new Uri("https://localhost/Observation-1.ndjson"), sequence: 1);
            ExportFileInfo file4 = new ExportFileInfo(resourceType2, new Uri("https://localhost/Observation-2.ndjson"), sequence: 2);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file2, file1 });
            _exportJobRecord.Output.Add(resourceType2, new List<ExportFileInfo>() { file4, file3 });

            await _exportFileManager.WriteToFile(resourceType2, "partId", new byte[] { 1 }, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file4.FileUri), Arg.Is(_cancellationTokenNone));
        }

        [Fact]
        public async Task GivenNoFilesInOutput_WhenWriteToFile_ThenDoesNotCallOpenFile()
        {
            await _exportFileManager.WriteToFile("Patient", "partId", new byte[] { 1 }, _cancellationTokenNone);

            await _exportDestinationClient.DidNotReceiveWithAnyArgs().OpenFileAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenNoFilesInOutput_WhenWriteToFile_ThenCreatesNewFile()
        {
            await _exportFileManager.WriteToFile("Patient", "partId", new byte[] { 1 }, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-1.ndjson"), Arg.Is(_cancellationTokenNone));
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public async Task GivenDataExceedsFileSizeLimit_WhenWriteToFile_ThenCreatesNewFileAfterWriting()
        {
            byte[] data = new byte[2 * 1024 * 1024];
            await _exportFileManager.WriteToFile("Patient", "partId", data, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-1.ndjson"), Arg.Is(_cancellationTokenNone));
            await _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is("partId"), Arg.Is(data), Arg.Is(_cancellationTokenNone));
            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-2.ndjson"), Arg.Is(_cancellationTokenNone));

            Assert.Equal(2, _exportJobRecord.Output["Patient"].Count);
        }
    }
}

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
        private readonly string _exportJobConfigurationFormat;

        public ExportFileManagerTests()
        {
            _exportDestinationClient = Substitute.For<IExportDestinationClient>();
            _exportDestinationClient
                .CreateFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<Uri>(callInfo => new Uri("https://localhost/" + callInfo.ArgAt<string>(0)));

            _exportJobConfigurationFormat = $"{ExportFormatTags.ResourceName}";
            _exportJobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                _exportJobConfigurationFormat,
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

            await _exportFileManager.WriteToFile(resourceType, "test", _cancellationTokenNone);

            _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file3.FileUri));
        }

        [Fact]
        public async Task GivenMultipleFilesOutOfOrderForResourceTypeInOutput_WhenWriteToFile_ThenWritesToLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);
            ExportFileInfo file3 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-3.ndjson"), sequence: 3);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file2, file3, file1 });

            await _exportFileManager.WriteToFile(resourceType, "test", _cancellationTokenNone);

            _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file3.FileUri));
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

            await _exportFileManager.WriteToFile(resourceType2, "test", _cancellationTokenNone);

            _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file4.FileUri));
        }

        [Fact]
        public async Task GivenNoFilesInOutput_WhenWriteToFile_ThenDoesNotCallOpenFile()
        {
            await _exportFileManager.WriteToFile("Patient", "test", _cancellationTokenNone);

            _exportDestinationClient.DidNotReceiveWithAnyArgs().OpenFileAsync(Arg.Any<Uri>());
        }

        [Fact]
        public async Task GivenNoFilesInOutput_WhenWriteToFile_ThenCreatesNewFile()
        {
            await _exportFileManager.WriteToFile("Patient", "test", _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-1.ndjson"), Arg.Is(_cancellationTokenNone));
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public async Task GivenDataExceedsFileSizeLimit_WhenWriteToFile_ThenDoesNotCreateNewFileAfterWriting()
        {
            string data = "other test";
            await _exportFileManager.WriteToFile("Patient", data, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-1.ndjson"), Arg.Is(_cancellationTokenNone));
            _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is(data), Arg.Is(_cancellationTokenNone));
            await _exportDestinationClient.DidNotReceive().CreateFileAsync(Arg.Is("Patient-2.ndjson"), Arg.Is(_cancellationTokenNone));

            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public async Task GivenDataExceedsFileSizeLimit_WhenWriteToFile_ThenCreatesNewFileAfterNextWriteCall()
        {
            string data = "other test";
            string data2 = "more testing";

            await _exportFileManager.WriteToFile("Patient", data, _cancellationTokenNone);
            await _exportFileManager.WriteToFile("Patient", data2, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-1.ndjson"), Arg.Is(_cancellationTokenNone));
            _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is(data), Arg.Is(_cancellationTokenNone));
            _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is(data2), Arg.Is(_cancellationTokenNone));
            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient-2.ndjson"), Arg.Is(_cancellationTokenNone));

            Assert.Equal(2, _exportJobRecord.Output["Patient"].Count);
        }

        [Fact]
        public async Task GivenExportJobRecordV1_WhenWriteToFile_ThenNewFileDoesNotHaveSequence()
        {
            InitializeManagerWithV1ExportJobRecord();

            await _exportFileManager.WriteToFile("Patient", "test", _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient.ndjson"), Arg.Is(_cancellationTokenNone));
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public async Task GivenExportJobRecordV1AndNoRollingFilzeSizeLimit_WhenWriteToFile_ThenDataWrittenToOneFile()
        {
            InitializeManagerWithV1ExportJobRecord();

            string data = "other test";
            await _exportFileManager.WriteToFile("Patient", data, _cancellationTokenNone);
            await _exportFileManager.WriteToFile("Patient", data, _cancellationTokenNone);

            await _exportDestinationClient.Received(1).CreateFileAsync(Arg.Is("Patient.ndjson"), Arg.Is(_cancellationTokenNone));
            _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is(data), Arg.Is(_cancellationTokenNone));
            _exportDestinationClient.Received(1).WriteFilePartAsync(Arg.Any<Uri>(), Arg.Is(data), Arg.Is(_cancellationTokenNone));
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public async Task GivenExportJobRecordV1WithFilesInOutput_WhenWriteToFile_ThenAllFilesAreOpened()
        {
            InitializeManagerWithV1ExportJobRecord();

            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient.ndjson"), sequence: 0);

            string resourceType2 = "Observation";
            ExportFileInfo file2 = new ExportFileInfo(resourceType2, new Uri("https://localhost/Observation.ndjson"), sequence: 0);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file1 });
            _exportJobRecord.Output.Add(resourceType2, new List<ExportFileInfo>() { file2 });

            await _exportFileManager.WriteToFile("Claim", "test", _cancellationTokenNone);

            _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file1.FileUri));
            _exportDestinationClient.Received(1).OpenFileAsync(Arg.Is(file2.FileUri));
        }

        private void InitializeManagerWithV1ExportJobRecord()
        {
            _exportJobRecord = new ExportJobRecord(
               new Uri("https://localhost/$export"),
               ExportJobType.All,
               _exportJobConfigurationFormat,
               resourceType: null,
               filters: null,
               "hash",
               rollingFileSizeInMB: 0,
               schemaVersion: 1);
            _exportFileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);
        }
    }
}

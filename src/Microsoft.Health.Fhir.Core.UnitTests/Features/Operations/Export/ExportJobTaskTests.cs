// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////using System;
////using System.Threading;
////using System.Threading.Tasks;
////using Microsoft.Extensions.Logging.Abstractions;
////using Microsoft.Health.Fhir.Core.Configs;
////using Microsoft.Health.Fhir.Core.Features.Operations;
////using Microsoft.Health.Fhir.Core.Features.Operations.Export;
////using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
////using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
////using Microsoft.Health.Fhir.Core.Features.Persistence;
////using Microsoft.Health.Fhir.Core.Features.Search;
////using Microsoft.Health.Fhir.Core.Features.SecretStore;
////using Microsoft.Health.Fhir.Core.Messages.Export;
////using NSubstitute;
////using Xunit;

////namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
////{
////    public class ExportJobTaskTests
////    {
////        private static readonly ExportJobRecord _exportJobRecord = new ExportJobRecord(
////            new CreateExportRequest(new Uri("https://localhost/ExportJob/"), "destinationType", "destinationConnection"), "hash");

////        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

////        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
////        private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
////        private readonly ExportJobConfiguration _exportJobConfiguration = new ExportJobConfiguration();
////        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
////        private readonly IResourceToNdjsonBytesSerializer _resourceToNdjsonSerializer = Substitute.For<IResourceToNdjsonBytesSerializer>();
////        private readonly IExportDestinationClientFactory _exportDestinationClientFactory = Substitute.For<IExportDestinationClientFactory>();

////        private ExportJobTask _exportJobTask;

////        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
////        private CancellationToken _cancellationToken;

////        public ExportJobTaskTests()
////        {
////            _cancellationToken = _cancellationTokenSource.Token;

////            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(x => new ExportJobOutcome(_exportJobRecord, _weakETag));

////            _exportJobTask = new ExportJobTask(
////                _fhirOperationDataStore,
////                _secretStore,
////                _exportExecutor,
////                NullLogger<ExportJobTask>.Instance);
////        }

////        [Fact]
////        public async Task GivenAJobCompletedSuccessfully_WhenExecuted_ThenStatusShouldBeUpdatedToCompleted()
////        {
////            bool capturedStatusUpdate = false;

////            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Completed), _weakETag, _cancellationToken).Returns(_ =>
////            {
////                // NSubstitute call capture was not working reliably so manually capture the update for now.
////                capturedStatusUpdate = true;

////                return new ExportJobOutcome(_exportJobRecord, _weakETag);
////            });

////            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

////            Assert.True(capturedStatusUpdate);
////            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
////        }

////        [Fact]
////        public async Task GivenAJobFailed_WhenExecuted_ThenStatusShouldBeUpdatedToFailed()
////        {
////            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Completed), _weakETag, _cancellationToken).Returns(_ =>
////            {
////                return Task.FromException(new Exception());
////            });

////            bool capturedStatusUpdate = false;

////            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Failed), _weakETag, _cancellationToken).Returns(_ =>
////            {
////                capturedStatusUpdate = true;

////                return new ExportJobOutcome(_exportJobRecord, _weakETag);
////            });

////            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

////            Assert.True(capturedStatusUpdate);
////            Assert.Equal(OperationStatus.Failed, _exportJobRecord.Status);
////        }

////        [Fact]
////        public async Task GivenTaskCouldNotBeAcquired_WhenExecuted_ThenNoFurtherActionShouldBeTaken()
////        {
////            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(_ => Task.Run(new Func<Task<ExportJobOutcome>>(() => throw new JobConflictException())));

////            await _exportJobTask.ExecuteAsync(_exportJobRecord, _weakETag, _cancellationToken);

////            await _fhirOperationDataStore.ReceivedWithAnyArgs(1).UpdateExportJobAsync(null, null, _cancellationToken);
////        }
////    }
////}

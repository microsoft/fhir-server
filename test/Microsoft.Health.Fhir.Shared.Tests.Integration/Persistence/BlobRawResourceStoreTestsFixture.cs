// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Fhir.Blob.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

internal class BlobRawResourceStoreTestsFixture : IAsyncLifetime
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IOptionsMonitor<BlobContainerConfiguration> _optionsMonitor;

    internal BlobRawResourceStoreTestsFixture()
    {
        _blobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        _optionsMonitor = Substitute.For<IOptionsMonitor<BlobContainerConfiguration>>();
        _optionsMonitor.CurrentValue.Returns(new BlobContainerConfiguration() { ContainerName = "fhirresources" });
        var blobStoreClient = new BlobStoreClient(_blobServiceClient, _optionsMonitor, NullLogger<BlobStoreClient>.Instance);
        var blobRawResourceStore = new BlobRawResourceStore(blobStoreClient, NullLogger<BlobRawResourceStore>.Instance, Options.Create(new BlobOperationOptions()), new IO.RecyclableMemoryStreamManager());
        RawResourceStore = blobRawResourceStore;
    }

    internal IRawResourceStore RawResourceStore { get; private set; }

    public async Task DisposeAsync()
    {
        // await _blobServiceClient.GetBlobContainerClient(_optionsMonitor.CurrentValue.ContainerName).DeleteIfExistsAsync();
        // TODO: This is a temporary workaround to avoid container being deleted by tests running in parallel.
        await Task.Delay(100);
    }

    public async Task InitializeAsync()
    {
        await _blobServiceClient.GetBlobContainerClient(_optionsMonitor.CurrentValue.ContainerName).CreateIfNotExistsAsync();
    }
}

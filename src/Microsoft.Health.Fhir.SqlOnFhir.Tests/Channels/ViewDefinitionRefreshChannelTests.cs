// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.Subscriptions.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Channels;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionRefreshChannel"/>.
/// </summary>
public class ViewDefinitionRefreshChannelTests
{
    private readonly IViewDefinitionMaterializer _materializer;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ViewDefinitionRefreshChannel _channel;

    private const string ViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    public ViewDefinitionRefreshChannelTests()
    {
        _materializer = Substitute.For<IViewDefinitionMaterializer>();
        _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        _subscriptionManager = Substitute.For<IViewDefinitionSubscriptionManager>();

        var config = Options.Create(new SqlOnFhirMaterializationConfiguration { DefaultTarget = MaterializationTarget.SqlServer });
        var factory = new MaterializerFactory(
            _materializer,
            config,
            NullLogger<MaterializerFactory>.Instance);

        _channel = new ViewDefinitionRefreshChannel(
            factory,
            _subscriptionManager,
            _resourceDeserializer,
            NullLogger<ViewDefinitionRefreshChannel>.Instance);
    }

    [Fact]
    public async Task GivenChangedResource_WhenPublished_ThenMaterializerUpsertCalled()
    {
        // Arrange
        var wrapper = CreateResourceWrapper("Patient", "p1", isDeleted: false);
        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());
        _resourceDeserializer.Deserialize(wrapper).Returns(mockElement);
        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var subscriptionInfo = CreateSubscriptionInfo();

        // Act
        await _channel.PublishAsync(
            new[] { wrapper },
            subscriptionInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert
        await _materializer.Received(1).UpsertResourceAsync(
            ViewDefinitionJson,
            "patient_demographics",
            mockElement,
            "Patient/p1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenDeletedResource_WhenPublished_ThenMaterializerDeleteCalled()
    {
        // Arrange
        var wrapper = CreateResourceWrapper("Patient", "p1", isDeleted: true);
        var subscriptionInfo = CreateSubscriptionInfo();

        // Act
        await _channel.PublishAsync(
            new[] { wrapper },
            subscriptionInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — should call DeleteResourceAsync, NOT UpsertResourceAsync
        await _materializer.Received(1).DeleteResourceAsync(
            "patient_demographics",
            "Patient/p1",
            Arg.Any<CancellationToken>());

        await _materializer.DidNotReceive().UpsertResourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMultipleResources_WhenPublished_ThenEachResourceProcessed()
    {
        // Arrange
        var wrappers = new[]
        {
            CreateResourceWrapper("Patient", "p1", isDeleted: false),
            CreateResourceWrapper("Patient", "p2", isDeleted: false),
            CreateResourceWrapper("Patient", "p3", isDeleted: true),
        };

        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());
        _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(mockElement);
        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var subscriptionInfo = CreateSubscriptionInfo();

        // Act
        await _channel.PublishAsync(
            wrappers,
            subscriptionInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — 2 upserts + 1 delete
        await _materializer.Received(2).UpsertResourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _materializer.Received(1).DeleteResourceAsync(
            "patient_demographics", "Patient/p3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMissingProperties_WhenPublished_ThenNoMaterializationAttempted()
    {
        // Arrange — subscription without ViewDefinition properties
        var wrapper = CreateResourceWrapper("Patient", "p1", isDeleted: false);
        var subscriptionInfo = CreateSubscriptionInfo(includeProperties: false);

        // Act
        await _channel.PublishAsync(
            new[] { wrapper },
            subscriptionInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — nothing should be called
        await _materializer.DidNotReceive().UpsertResourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await _materializer.DidNotReceive().DeleteResourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMaterializerFailure_WhenPublished_ThenProcessingContinues()
    {
        // Arrange
        var wrappers = new[]
        {
            CreateResourceWrapper("Patient", "p1", isDeleted: false),
            CreateResourceWrapper("Patient", "p2", isDeleted: false),
        };

        var mockElement = Substitute.For<ResourceElement>(Substitute.For<Hl7.Fhir.ElementModel.ITypedElement>());
        _resourceDeserializer.Deserialize(Arg.Any<ResourceWrapper>()).Returns(mockElement);

        // First call throws, second succeeds
        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), "Patient/p1", Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("Test failure"));

        _materializer.UpsertResourceAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), "Patient/p2", Arg.Any<CancellationToken>())
            .Returns(1);

        var subscriptionInfo = CreateSubscriptionInfo();

        // Act — should not throw
        await _channel.PublishAsync(
            wrappers,
            subscriptionInfo,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert — both resources were attempted
        await _materializer.Received(2).UpsertResourceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ResourceElement>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public Task GivenValidProperties_WhenHandshake_ThenNoExceptionThrown()
    {
        var subscriptionInfo = CreateSubscriptionInfo();

        // Should not throw
        return _channel.PublishHandShakeAsync(subscriptionInfo, CancellationToken.None);
    }

    [Fact]
    public async Task GivenMissingProperties_WhenHandshake_ThenExceptionThrown()
    {
        var subscriptionInfo = CreateSubscriptionInfo(includeProperties: false);

        await Assert.ThrowsAsync<Subscriptions.Validation.SubscriptionException>(
            () => _channel.PublishHandShakeAsync(subscriptionInfo, CancellationToken.None));
    }

    private static SubscriptionInfo CreateSubscriptionInfo(bool includeProperties = true)
    {
        var channelInfo = new ChannelInfo
        {
            ChannelType = SubscriptionChannelType.ViewDefinitionRefresh,
            MaxCount = 100,
        };

        if (includeProperties)
        {
            channelInfo.Properties = new Dictionary<string, string>
            {
                ["viewDefinitionJson"] = ViewDefinitionJson,
                ["viewDefinitionName"] = "patient_demographics",
            };
        }

        return new SubscriptionInfo(
            includeProperties ? "Patient?" : "Patient?",
            channelInfo,
            new Uri("http://example.com/topic/patient-demographics"),
            "sub-1",
            SubscriptionStatus.Active);
    }

    private static ResourceWrapper CreateResourceWrapper(string resourceType, string resourceId, bool isDeleted)
    {
        return new ResourceWrapper(
            resourceId,
            "1",
            resourceType,
            new RawResource("{ }", FhirResourceFormat.Json, true),
            null,
            DateTimeOffset.UtcNow,
            isDeleted,
            null,
            null,
            null);
    }
}

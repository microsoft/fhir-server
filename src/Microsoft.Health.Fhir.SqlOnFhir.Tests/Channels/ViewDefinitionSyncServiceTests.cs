// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Channels;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionSyncService"/>.
/// Verifies that the sync service correctly extracts ViewDefinition JSON from Library resources
/// regardless of whether the ITypedElement model returns byte[] or base64 strings for data.
/// </summary>
public class ViewDefinitionSyncServiceTests
{
    private const string ViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    private const string BloodPressureViewDefinitionJson = """
        {
            "name": "us_core_blood_pressures",
            "resource": "Observation",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    private readonly ISearchService _searchService;
    private readonly IResourceDeserializer _resourceDeserializer;
    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ViewDefinitionSyncService _syncService;

    public ViewDefinitionSyncServiceTests()
    {
        _searchService = Substitute.For<ISearchService>();
        _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        _subscriptionManager = Substitute.For<IViewDefinitionSubscriptionManager>();

        var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
        scopedSearchService.Value.Returns(_searchService);

        _syncService = new ViewDefinitionSyncService(
            () => scopedSearchService,
            _resourceDeserializer,
            _subscriptionManager,
            NullLogger<ViewDefinitionSyncService>.Instance);
    }

    /// <summary>
    /// Tests that ViewDefinition JSON is correctly extracted from a Library resource
    /// that uses the POCO element model (where base64Binary Value returns byte[], not string).
    /// This is the bug scenario: after restart, the sync service deserializes Library resources
    /// using the POCO model, and Attachment.Data.Value is byte[] rather than a base64 string.
    /// </summary>
    [Fact]
    public async Task GivenLibraryWithPocoElementModel_WhenSyncing_ThenViewDefinitionIsAdopted()
    {
        // Arrange: Create a Library resource with ViewDefinition content
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        SetupSearchReturnsLibrary(library, "lib-1");
        _subscriptionManager.GetRegistration("patient_demographics").Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act: Start the service to create the timer, then trigger initialization
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        // Wait for the timer callback to execute
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: The ViewDefinition should have been adopted
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            Arg.Any<ViewDefinitionStatus>(),
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests that multiple ViewDefinition Library resources are all adopted during sync.
    /// </summary>
    [Fact]
    public async Task GivenMultipleLibraries_WhenSyncing_ThenAllViewDefinitionsAdopted()
    {
        // Arrange
        Library lib1 = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        Library lib2 = BuildViewDefinitionLibrary(BloodPressureViewDefinitionJson, "us_core_blood_pressures", "Observation");

        SetupSearchReturnsLibraries(
            (lib1, "lib-1"),
            (lib2, "lib-2"));

        _subscriptionManager.GetRegistration(Arg.Any<string>()).Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            Arg.Any<ViewDefinitionStatus>(),
            Arg.Any<IReadOnlyList<string>>());
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("us_core_blood_pressures")),
            Arg.Is("lib-2"),
            Arg.Any<CancellationToken>(),
            Arg.Any<ViewDefinitionStatus>(),
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests that existing registrations are not re-adopted.
    /// </summary>
    [Fact]
    public async Task GivenAlreadyRegisteredViewDef_WhenSyncing_ThenNotAdoptedAgain()
    {
        // Arrange
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        SetupSearchReturnsLibrary(library, "lib-1");

        var existingRegistration = new ViewDefinitionRegistration
        {
            ViewDefinitionJson = ViewDefinitionJson.Trim(),
            ViewDefinitionName = "patient_demographics",
            ResourceType = "Patient",
            Status = ViewDefinitionStatus.Active,
        };

        _subscriptionManager.GetRegistration("patient_demographics").Returns(existingRegistration);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration> { existingRegistration });

        // Act
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: AdoptAsync should NOT be called since it's already registered with same content
        await _subscriptionManager.DidNotReceive().AdoptAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<ViewDefinitionStatus>(),
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests the startup race condition: Handle (notification) fires before ExecuteAsync creates the timer.
    /// The fix ensures ExecuteAsync detects that initialization already happened and starts the timer.
    /// </summary>
    [Fact]
    public async Task GivenNotificationBeforeExecuteAsync_WhenStarting_ThenViewDefinitionsStillAdopted()
    {
        // Arrange
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        SetupSearchReturnsLibrary(library, "lib-1");
        _subscriptionManager.GetRegistration("patient_demographics").Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act: Handle fires BEFORE StartAsync — simulating the race condition
        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        // Now StartAsync/ExecuteAsync runs — should detect _isInitialized and start timer
        await _syncService.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: The ViewDefinition should still be adopted despite the race
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            Arg.Any<ViewDefinitionStatus>(),
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests that a ViewDefinition with "populating" materialization-status extension
    /// is adopted with Populating status (not Active) on restart.
    /// </summary>
    [Fact]
    public async Task GivenLibraryWithPopulatingStatus_WhenSyncing_ThenAdoptedWithPopulatingStatus()
    {
        // Arrange: Build a Library with the materialization-status extension set to "populating"
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        library.Extension.Add(new Extension(
            ViewDefinitionSubscriptionManager.MaterializationStatusExtensionUrl,
            new Code("populating")));

        SetupSearchReturnsLibrary(library, "lib-1");
        _subscriptionManager.GetRegistration("patient_demographics").Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: AdoptAsync should be called with Populating status
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            ViewDefinitionStatus.Populating,
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests that a ViewDefinition with "active" materialization-status extension
    /// is adopted with Active status on restart.
    /// </summary>
    [Fact]
    public async Task GivenLibraryWithActiveStatus_WhenSyncing_ThenAdoptedWithActiveStatus()
    {
        // Arrange
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");
        library.Extension.Add(new Extension(
            ViewDefinitionSubscriptionManager.MaterializationStatusExtensionUrl,
            new Code("active")));

        SetupSearchReturnsLibrary(library, "lib-1");
        _subscriptionManager.GetRegistration("patient_demographics").Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: AdoptAsync should be called with Active status
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            ViewDefinitionStatus.Active,
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tests that a Library without the materialization-status extension (backward compatibility)
    /// defaults to Active status.
    /// </summary>
    [Fact]
    public async Task GivenLibraryWithoutStatusExtension_WhenSyncing_ThenDefaultsToActiveStatus()
    {
        // Arrange: Build a Library WITHOUT the materialization-status extension
        Library library = BuildViewDefinitionLibrary(ViewDefinitionJson, "patient_demographics", "Patient");

        SetupSearchReturnsLibrary(library, "lib-1");
        _subscriptionManager.GetRegistration("patient_demographics").Returns((ViewDefinitionRegistration?)null);
        _subscriptionManager.GetAllRegistrations().Returns(new List<ViewDefinitionRegistration>());

        // Act
        await _syncService.StartAsync(CancellationToken.None);

        await _syncService.Handle(
            new Microsoft.Health.Fhir.Core.Messages.Search.SearchParametersInitializedNotification(),
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert: AdoptAsync should be called with Active status (the default)
        await _subscriptionManager.Received().AdoptAsync(
            Arg.Is<string>(json => json.Contains("patient_demographics")),
            Arg.Is("lib-1"),
            Arg.Any<CancellationToken>(),
            ViewDefinitionStatus.Active,
            Arg.Any<IReadOnlyList<string>>());

        await _syncService.StopAsync(CancellationToken.None);
    }

    private static Library BuildViewDefinitionLibrary(string viewDefJson, string name, string resourceType)
    {
        return new Library
        {
            Id = $"lib-{name}",
            Meta = new Meta
            {
                Profile = new List<string> { ViewDefinitionSubscriptionManager.ViewDefinitionLibraryProfile },
            },
            Name = name,
            Title = $"ViewDefinition: {name}",
            Status = PublicationStatus.Active,
            Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/library-type", "logic-library"),
            Description = new Markdown($"SQL on FHIR v2 ViewDefinition for {resourceType} resources."),
            Content = new List<Attachment>
            {
                new Attachment
                {
                    ContentType = ViewDefinitionSubscriptionManager.ViewDefinitionContentType,
                    Data = Encoding.UTF8.GetBytes(viewDefJson),
                },
            },
        };
    }

    private void SetupSearchReturnsLibrary(Library library, string resourceId)
    {
        SetupSearchReturnsLibraries((library, resourceId));
    }

    private void SetupSearchReturnsLibraries(params (Library Library, string ResourceId)[] libraries)
    {
        var entries = new List<SearchResultEntry>();

        foreach (var (library, resourceId) in libraries)
        {
            // Serialize the Library to JSON (simulating what's stored in the DB)
            string json = new FhirJsonSerializer().SerializeToString(library);

            var wrapper = new ResourceWrapper(
                resourceId,
                "1",
                "Library",
                new RawResource(json, FhirResourceFormat.Json, isMetaSet: true),
                null,
                DateTimeOffset.UtcNow,
                false,
                null,
                null,
                null);

            // Simulate the POCO-based deserialization that the production code uses.
            // This produces PocoTypedElement where base64Binary Value returns byte[].
            var resourceElement = new ResourceElement(library.ToTypedElement());
            _resourceDeserializer.Deserialize(wrapper).Returns(resourceElement);

            entries.Add(new SearchResultEntry(wrapper));
        }

        var searchResult = new SearchResult(entries, null, null, new List<Tuple<string, string>>());

        _searchService.SearchAsync(
            "Library",
            Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
            Arg.Any<CancellationToken>())
            .Returns(searchResult);
    }
}

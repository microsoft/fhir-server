// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Channels;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionLibraryRegistrationBehavior"/>.
/// Verifies that the materialization target extension is correctly extracted from Library
/// resources, particularly for choice-type element navigation in ITypedElement.
/// </summary>
public class ViewDefinitionLibraryRegistrationBehaviorTests
{
    private const string ViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    private readonly IViewDefinitionSubscriptionManager _subscriptionManager;
    private readonly ViewDefinitionLibraryRegistrationBehavior _behavior;

    public ViewDefinitionLibraryRegistrationBehaviorTests()
    {
        _subscriptionManager = Substitute.For<IViewDefinitionSubscriptionManager>();
        _subscriptionManager.RegisterAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<MaterializationTarget?>())
            .Returns(new ViewDefinitionRegistration
            {
                ViewDefinitionJson = ViewDefinitionJson,
                ViewDefinitionName = "patient_demographics",
                ResourceType = "Patient",
                Target = MaterializationTarget.SqlServer,
            });

        _behavior = new ViewDefinitionLibraryRegistrationBehavior(
            _subscriptionManager,
            NullLogger<ViewDefinitionLibraryRegistrationBehavior>.Instance);
    }

    [Theory]
    [InlineData("Fabric", MaterializationTarget.Fabric)]
    [InlineData("SqlServer", MaterializationTarget.SqlServer)]
    [InlineData("Parquet", MaterializationTarget.Parquet)]
    public async Task GivenLibraryWithTargetExtension_WhenCreated_ThenTargetIsPassedToSubscriptionManager(
        string targetCode, MaterializationTarget expectedTarget)
    {
        // Arrange
        Library library = BuildViewDefinitionLibrary(targetCode);
        ResourceElement resourceElement = ToResourceElement(library);
        var request = new CreateResourceRequest(resourceElement);

        UpsertResourceResponse fakeResponse = BuildFakeResponse("viewdef-patient-demographics");

        // Act
        await _behavior.Handle(
            request,
            _ => Task.FromResult(fakeResponse),
            CancellationToken.None);

        // Assert — verify RegisterAsync was called with the correct target
        await _subscriptionManager.Received(1).RegisterAsync(
            Arg.Any<string>(),
            "viewdef-patient-demographics",
            Arg.Any<CancellationToken>(),
            expectedTarget);
    }

    [Fact]
    public async Task GivenLibraryWithoutTargetExtension_WhenCreated_ThenNullTargetIsPassedToSubscriptionManager()
    {
        // Arrange — no target extension
        Library library = BuildViewDefinitionLibrary(targetCode: null);
        ResourceElement resourceElement = ToResourceElement(library);
        var request = new CreateResourceRequest(resourceElement);

        UpsertResourceResponse fakeResponse = BuildFakeResponse("viewdef-patient-demographics");

        // Act
        await _behavior.Handle(
            request,
            _ => Task.FromResult(fakeResponse),
            CancellationToken.None);

        // Assert — target should be null (server decides default)
        await _subscriptionManager.Received(1).RegisterAsync(
            Arg.Any<string>(),
            "viewdef-patient-demographics",
            Arg.Any<CancellationToken>(),
            null);
    }

    private static Library BuildViewDefinitionLibrary(string? targetCode)
    {
        var library = new Library
        {
            Id = "viewdef-patient-demographics",
            Meta = new Meta
            {
                Profile = new[] { ViewDefinitionSubscriptionManager.ViewDefinitionLibraryProfile },
            },
            Name = "patient_demographics",
            Status = PublicationStatus.Active,
            Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/library-type", "logic-library"),
            Content = new List<Attachment>
            {
                new Attachment
                {
                    ContentType = "application/json+viewdefinition",
                    Data = Encoding.UTF8.GetBytes(ViewDefinitionJson),
                },
            },
        };

        if (targetCode != null)
        {
            library.Extension.Add(new Extension(
                ViewDefinitionSubscriptionManager.MaterializationTargetExtensionUrl,
                new Code(targetCode)));
        }

        return library;
    }

    private static ResourceElement ToResourceElement(Resource resource)
    {
        ITypedElement typedElement = resource.ToTypedElement();
        return new ResourceElement(typedElement);
    }

    private static UpsertResourceResponse BuildFakeResponse(string resourceId)
    {
        string json = $$"""{"resourceType": "Library", "id": "{{resourceId}}"}""";

        var rawResource = new RawResource(json, FhirResourceFormat.Json, true);

        var wrapper = new ResourceWrapper(
            resourceId,
            "1",
            "Library",
            rawResource,
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        var outcome = new SaveOutcome(
            new RawResourceElement(wrapper),
            SaveOutcomeType.Created);

        return new UpsertResourceResponse(outcome);
    }
}

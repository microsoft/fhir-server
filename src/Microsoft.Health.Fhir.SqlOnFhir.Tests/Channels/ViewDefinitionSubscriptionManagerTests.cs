// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Operations.ViewDefinitionRun;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlOnFhir.Channels;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Channels;

/// <summary>
/// Unit tests for <see cref="ViewDefinitionSubscriptionManager"/>.
/// Tests the Subscription resource building logic without requiring MediatR or a data store.
/// </summary>
public class ViewDefinitionSubscriptionManagerTests
{
    private const string PatientViewDefinitionJson = """
        {
            "name": "patient_demographics",
            "resource": "Patient",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }]
        }
        """;

    private const string BpViewDefinitionJson = """
        {
            "name": "us_core_blood_pressures",
            "resource": "Observation",
            "select": [{ "column": [{ "name": "id", "path": "id" }] }],
            "where": [{"path": "code.coding.exists(system='http://loinc.org' and code='85354-9')"}]
        }
        """;

    [Fact]
    public void GivenPatientViewDef_WhenBuildingSubscription_ThenResourceTypeFilterIsPatient()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        // Verify criteria extension contains Patient resource type filter
        Assert.NotNull(sub.CriteriaElement);
        var filterExt = sub.CriteriaElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-filter-criteria"));
        Assert.NotNull(filterExt);
        Assert.Equal("Patient?", ((FhirString)filterExt!.Value).Value);
    }

    [Fact]
    public void GivenObservationViewDef_WhenBuildingSubscription_ThenResourceTypeFilterIsObservation()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            BpViewDefinitionJson,
            "us_core_blood_pressures",
            "Observation");

        var filterExt = sub.CriteriaElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-filter-criteria"));
        Assert.NotNull(filterExt);
        Assert.Equal("Observation?", ((FhirString)filterExt!.Value).Value);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenChannelTypeIsViewDefinitionRefresh()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var channelTypeExt = sub.Channel.TypeElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-channel-type"));
        Assert.NotNull(channelTypeExt);
        Assert.Equal("view-definition-refresh", ((Coding)channelTypeExt!.Value).Code);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenHeadersContainViewDefinitionMetadata()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.NotNull(sub.Channel.Header);
        Assert.Equal(2, sub.Channel.Header.Count());

        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionName: "));
        Assert.Contains(sub.Channel.Header, h => h.StartsWith("viewDefinitionJson: "));

        string nameHeader = sub.Channel.Header.First(h => h.StartsWith("viewDefinitionName: "));
        Assert.Equal("viewDefinitionName: patient_demographics", nameHeader);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenBackportProfileIsSet()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.NotNull(sub.Meta?.Profile);
        Assert.Contains(
            "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription",
            sub.Meta.Profile);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenStatusIsRequested()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.Equal(Subscription.SubscriptionStatus.Requested, sub.Status);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenEndpointIsInternalUri()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        Assert.Equal("internal://sqlfhir/patient_demographics", sub.Channel.Endpoint);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenPayloadIsFullResource()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var payloadExt = sub.Channel.PayloadElement.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-payload-content"));
        Assert.NotNull(payloadExt);
        Assert.Equal("full-resource", ((Code)payloadExt!.Value).Value);
    }

    [Fact]
    public void GivenViewDef_WhenBuildingSubscription_ThenMaxCountExtensionPresent()
    {
        Subscription sub = ViewDefinitionSubscriptionManager.BuildSubscriptionResource(
            PatientViewDefinitionJson,
            "patient_demographics",
            "Patient");

        var maxCountExt = sub.Channel.Extension.FirstOrDefault(
            e => e.Url.Contains("backport-max-count"));
        Assert.NotNull(maxCountExt);
        Assert.Equal(100, ((PositiveInt)maxCountExt!.Value).Value);
    }

    [Fact]
    public async Task GivenPopulationComplete_WhenHandled_ThenLibraryResourceWrittenWithActiveStatus()
    {
        // Initialize FHIR model provider (required for FhirJsonParser / ToTypedElement in production code)
        Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
        ModelInfoProvider.SetProvider(
            MockModelInfoProviderBuilder.Create(FhirSpecification.R4)
                .AddKnownTypes("Library")
                .Build());

        // Arrange — build a Library resource with meta.versionId (simulating what the DB returns)
        var library = new Library
        {
            Id = "lib-123",
            Meta = new Meta
            {
                VersionId = "2",
                LastUpdated = DateTimeOffset.UtcNow,
                Profile = new[] { ViewDefinitionSubscriptionManager.ViewDefinitionLibraryProfile },
            },
            Name = "patient_demographics",
            Status = PublicationStatus.Active,
            Type = new CodeableConcept(
                "http://terminology.hl7.org/CodeSystem/library-type",
                "logic-library"),
            Content = new List<Attachment>
            {
                new Attachment
                {
                    ContentType = ViewDefinitionSubscriptionManager.ViewDefinitionContentType,
                    Data = System.Text.Encoding.UTF8.GetBytes(PatientViewDefinitionJson),
                },
            },
            Extension = new List<Extension>
            {
                new Extension(
                    ViewDefinitionSubscriptionManager.MaterializationStatusExtensionUrl,
                    new Code("populating")),
                new Extension(
                    ViewDefinitionSubscriptionManager.MaterializationTargetExtensionUrl,
                    new Code("SqlServer")),
            },
        };

        string libraryJson = new FhirJsonSerializer().SerializeToString(library);

        // Build raw resource for the GET response
        var rawResource = new RawResource(
            libraryJson,
            FhirResourceFormat.Json,
            isMetaSet: true);
        var wrapper = new ResourceWrapper(
            "lib-123",
            "2",
            "Library",
            rawResource,
            new ResourceRequest("PUT"),
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        // Build upsert response
        var upsertRawResource = new RawResource(
            "{}",
            FhirResourceFormat.Json,
            isMetaSet: true);
        var upsertWrapper = new ResourceWrapper(
            "lib-123",
            "3",
            "Library",
            upsertRawResource,
            new ResourceRequest("PUT"),
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        // Use a spy mediator that captures the UpsertResourceRequest
        UpsertResourceRequest? capturedUpsertRequest = null;
        var spyMediator = new SpyMediator(
            getResourceResponse: new GetResourceResponse(new RawResourceElement(wrapper)),
            upsertResourceResponse: new UpsertResourceResponse(
                new SaveOutcome(new RawResourceElement(upsertWrapper), SaveOutcomeType.Updated)),
            captureUpsert: req => capturedUpsertRequest = req);

        // Use a real ServiceCollection to properly resolve IMediator via GetRequiredService
        var services = new ServiceCollection();
        services.AddSingleton<IMediator>(spyMediator);
        var builtProvider = services.BuildServiceProvider();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(builtProvider);
        scopeFactory.CreateScope().Returns(scope);

        var schemaManager = Substitute.For<IViewDefinitionSchemaManager>();
        schemaManager
            .TableExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var config = Options.Create(new SqlOnFhirMaterializationConfiguration
        {
            DefaultTarget = MaterializationTarget.SqlServer,
        });

        var sqlMaterializer = Substitute.For<IViewDefinitionMaterializer>();
        var materializerFactory = new MaterializerFactory(
            sqlMaterializer,
            config,
            NullLogger<MaterializerFactory>.Instance);

        // Use a capturing logger to surface any errors from UpdateLibraryMaterializationStatusAsync
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Trace));
        var capturedErrors = new List<string>();
        var testLogger = new CapturingLogger(capturedErrors);

        var manager = new ViewDefinitionSubscriptionManager(
            scopeFactory,
            schemaManager,
            Substitute.For<IQueueClient>(),
            materializerFactory,
            config,
            testLogger);

        // Pre-populate a registration in the "Populating" state
        await manager.AdoptAsync(
            PatientViewDefinitionJson,
            "lib-123",
            CancellationToken.None,
            initialStatus: ViewDefinitionStatus.Populating);

        // Act — handle the population-complete notification
        var notification = new ViewDefinitionPopulationCompleteNotification(
            viewDefinitionName: "patient_demographics",
            success: true,
            rowsInserted: 42,
            libraryResourceId: "lib-123");

        await manager.Handle(notification, CancellationToken.None);

        // Assert — in-memory status should be Active
        ViewDefinitionRegistration? registration = manager.GetRegistration("patient_demographics");
        Assert.NotNull(registration);
        Assert.Equal(ViewDefinitionStatus.Active, registration!.Status);

        // Assert — no errors should have been logged during Library update.
        // If this fails, it reveals the actual exception that was being silently swallowed.
        string errors = string.Join(Environment.NewLine, capturedErrors);
        Assert.True(capturedErrors.Count == 0, $"Library update errors: {errors}");

        // Assert — no exception should have occurred in the SpyMediator
        Assert.Null(spyMediator.LastException);

        // Assert — Library upsert MUST have been called (the resource was written out)
        Assert.NotNull(capturedUpsertRequest);

        // Assert — the upserted Library must have the "active" status extension
        Hl7.Fhir.ElementModel.ITypedElement upsertedElement = capturedUpsertRequest!.Resource.Instance;
        var statusExtension = upsertedElement
            .Children("extension")
            .FirstOrDefault(ext =>
                string.Equals(
                    ext.Children("url").FirstOrDefault()?.Value?.ToString(),
                    ViewDefinitionSubscriptionManager.MaterializationStatusExtensionUrl,
                    StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(statusExtension);

        string? statusValue = statusExtension!
            .Children("value").FirstOrDefault()?.Value?.ToString();
        Assert.Equal("active", statusValue);

        // Assert — meta.versionId must be cleared to prevent version conflicts
        var meta = upsertedElement.Children("meta").FirstOrDefault();
        Assert.NotNull(meta);
        var versionId = meta!.Children("versionId").FirstOrDefault();
        Assert.Null(versionId);
    }

    /// <summary>
    /// Simple MediatR spy that routes GET and Upsert requests to pre-configured responses,
    /// capturing the UpsertResourceRequest for assertion. Avoids NSubstitute's generic method
    /// matching issues with <see cref="IMediator.Send{TResponse}"/>.
    /// </summary>
    private sealed class SpyMediator : IMediator
    {
        private readonly GetResourceResponse _getResponse;
        private readonly UpsertResourceResponse _upsertResponse;
        private readonly Action<UpsertResourceRequest> _captureUpsert;

        public SpyMediator(
            GetResourceResponse getResourceResponse,
            UpsertResourceResponse upsertResourceResponse,
            Action<UpsertResourceRequest> captureUpsert)
        {
            _getResponse = getResourceResponse;
            _upsertResponse = upsertResourceResponse;
            _captureUpsert = captureUpsert;
        }

        public Exception? LastException { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (request is GetResourceRequest && _getResponse is TResponse getResp)
                {
                    return Task.FromResult(getResp);
                }

                if (request is UpsertResourceRequest upsertReq && _upsertResponse is TResponse upsertResp)
                {
                    _captureUpsert(upsertReq);
                    return Task.FromResult(upsertResp);
                }
            }
            catch (Exception ex)
            {
                LastException = ex;
                throw;
            }

            var error = new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
            LastException = error;
            throw error;
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(
            object notification,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }

    /// <summary>
    /// Logger that captures error messages for test assertions.
    /// </summary>
    private sealed class CapturingLogger : ILogger<ViewDefinitionSubscriptionManager>
    {
        private readonly List<string> _errors;

        public CapturingLogger(List<string> errors) => _errors = errors;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                string message = formatter(state, exception);
                if (exception != null)
                {
                    message += $" | Exception: {exception.GetType().Name}: {exception.Message}";
                }

                _errors.Add(message);
            }
        }
    }
}

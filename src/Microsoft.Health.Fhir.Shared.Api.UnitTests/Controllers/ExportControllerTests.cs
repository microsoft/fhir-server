// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.ArtifactStore;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportControllerTests
    {
        private ExportController _exportEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private static ArtifactStoreConfiguration _artifactStoreConfig = new ArtifactStoreConfiguration();
        private static FeatureConfiguration _featureConfiguration = new FeatureConfiguration();
        private static FeatureConfiguration _anonymizationEnabledFeatureConfiguration = new FeatureConfiguration() { SupportsAnonymizedExport = true };
        private static ExportJobConfiguration _exportEnabledJobConfiguration = new ExportJobConfiguration() { Enabled = true };

        private const string _testContainer = "testContainer";
        private const string _testConfig = "testConfig";
        private const string _testAnonymizationConfigCollectionReference = "testAnonymizationConfigCollectionReference";
        private const string _testAnonymizationConfigEtag = "testAnonymizationConfigEtag";

        public ExportControllerTests()
        {
            _exportEnabledController = GetController(_exportEnabledJobConfiguration, _featureConfiguration, _artifactStoreConfig);
            _exportEnabledController.ControllerContext = new ControllerContext(
               new ActionContext(
                   Substitute.For<HttpContext>(),
                   new RouteData(),
                   new ControllerActionDescriptor()));
        }

        [Fact]
        public async Task GivenAnExportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false }, _featureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.Export(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                isParallel: false,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null));
        }

        [Fact]
        public async Task GivenAnExportByResourceTypeRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false }, _featureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnExportByIdRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false }, _featureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceTypeById(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Group.ToString(),
                idParameter: "id"));
        }

        [Fact]
        public async Task GivenAnExportByResourceTypeRequest_WhenResourceTypeIsNotPatient_ThenRequestNotValidExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Observation.ToString()));
        }

        [Fact]
        public async Task GivenAnExportResourceTypeIdRequest_WhenResourceTypeIsNotGroup_ThenRequestNotValidExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.ExportResourceTypeById(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString(),
                idParameter: "id"));
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequest_WhenNoContainerName_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(_exportEnabledJobConfiguration, _anonymizationEnabledFeatureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: _testConfig,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequestWithAnonymizationConfigEtag_WhenNoAnonymizationConfig_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(_exportEnabledJobConfiguration, _anonymizationEnabledFeatureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: _testContainer,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: _testAnonymizationConfigEtag,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequestWithAnonymizationConfigCollectionReference_WhenNoAnonymizationConfig_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(_exportEnabledJobConfiguration, _anonymizationEnabledFeatureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: _testContainer,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: _testAnonymizationConfigCollectionReference,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequestWithAnonymizationConfigCollectionReference_WhenHasAnonymizationConfigEtag_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(_exportEnabledJobConfiguration, _anonymizationEnabledFeatureConfiguration, _artifactStoreConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: _testContainer,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: _testAnonymizationConfigCollectionReference,
                anonymizationConfigLocation: _testConfig,
                anonymizationConfigFileETag: _testAnonymizationConfigEtag,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnExportRequestWithHistoryOrDeletedIncluded_WhenHasTypeFilter_ThenRequestNotValidExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.Export(
                since: null,
                till: null,
                resourceType: ResourceType.Patient.ToString(),
                containerName: null,
                formatName: null,
                typeFilter: "Patient%3Factive%3Dtrue",
                includeAssociatedData: "_history"));

            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.Export(
                since: null,
                till: null,
                resourceType: ResourceType.Patient.ToString(),
                containerName: null,
                formatName: null,
                typeFilter: "Patient%3Factive%3Dtrue",
                includeAssociatedData: "_deleted"));
        }

        [Fact]
        public async Task GivenAnExportRequestWithDataIncluded_WhenKeyIsInvalid_ThenRequestNotValidExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.Export(
                since: null,
                till: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                typeFilter: null,
                includeAssociatedData: "_test"));
        }

        // We can configure OciArtifacts through three fields: LoginServer, ImageName and Digest
        // If ImageName and Digest are null, all images under the specified LoginSever are allowed to be used.
        // Similarly, if LoginSever and ImageName are specified and Digest is empty, all digests under the specified ImageName are allowed to be used.
        // If all three fields are provided, only the specified digest is allowed to be used.
        [Theory]
        [InlineData(null, null, null, "abc.azurecr.io/deidconfigs:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992")]
        [InlineData("abc.azurecr.io", null, null, "dummy.azurecr.io/deidconfigs:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992")]
        [InlineData("abc.azurecr.io", "configs", null, "abc.azurecr.io/deidconfigs:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992")]
        [InlineData("abc.azurecr.io", "deidconfigs", "sha256:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992", "abc.azurecr.io/deidconfigs:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992")]
        [InlineData("abc.azurecr.io", "deidconfigs", "sha256:2ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992", "abc.azurecr.io/deidconfigs@sha256:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992")]
        public async Task GivenAnAnonymizedExportRequestWithoutConfiguredImage_WhenValidBodySent_ThenContainerRegistryNotConfiguredExceptionShouldBeThrown(string loginServer, string imageName, string digest, string anonymizationConfigCollectionReference)
        {
            var ociArtifactInfo = new OciArtifactInfo
            {
                LoginServer = loginServer,
                ImageName = imageName,
                Digest = digest,
            };
            var artifactConfig = new ArtifactStoreConfiguration();
            artifactConfig.OciArtifacts.Add(ociArtifactInfo);
            var exportController = GetController(_exportEnabledJobConfiguration, _anonymizationEnabledFeatureConfiguration, artifactConfig);

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                till: null,
                resourceType: null,
                containerName: _testContainer,
                formatName: null,
                maxCount: 0,
                anonymizationConfigCollectionReference: anonymizationConfigCollectionReference,
                anonymizationConfigLocation: _testConfig,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Theory]
        [InlineData(true, false, null)]
        [InlineData(false, true, null)]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public async Task GivenASystemLevelExport_WhenRequestSentToMediator_CorrectIsParallelValueInRequest(bool isApiForFhir, bool expectedIsParallel, bool? inputIsParallelValue)
        {
            // Get export controller with specific runtime configuration (if needed).
            IFhirRuntimeConfiguration fhirConfig = isApiForFhir ? Substitute.For<AzureApiForFhirRuntimeConfiguration>() : Substitute.For<IFhirRuntimeConfiguration>();
            var exportController = GetController(_exportEnabledJobConfiguration, _featureConfiguration, _artifactStoreConfig, fhirConfig);

            // Setup additional dependencies needed for test execution.
            exportController.ControllerContext.HttpContext = new DefaultHttpContext();

            _fhirRequestContextAccessor.RequestContext = new FhirRequestContext(
               method: "export",
               uriString: "https://test.com/",
               baseUriString: "https://test.com/",
               correlationId: "export",
               requestHeaders: new Dictionary<string, StringValues>(),
               responseHeaders: new Dictionary<string, StringValues>());

            _urlResolver
                .ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new Uri("http://test.com/"));

            // Mock mediator call for CreateExportRequest - throw exception to fail test if we get unexpected value.
            _mediator
                .Send(Arg.Any<CreateExportRequest>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var request = callInfo.Arg<CreateExportRequest>();
                    if (request.IsParallel != expectedIsParallel)
                    {
                        throw new InvalidOperationException($"Expected isParallel value of {expectedIsParallel} but got {request.IsParallel}.");
                    }

                    return new CreateExportResponse("ExportTestJobId");
                });

            try
            {
                await exportController.Export(null, null, string.Empty, string.Empty, string.Empty, string.Empty, isParallel: inputIsParallelValue);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [Fact]
        public async Task GivenAnExportRequest_WhenProcessing_ThenCreateExportRequestShouldBeCreatedCorrectly()
        {
            await RunCreateExportRequestTest(
                (since, till, resourceType, groupId, containerName, typeFilter, formatName, isParallel, includeAssociatedData, maxCount, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag) =>
                    _exportEnabledController.Export(
                        since,
                        till,
                        resourceType,
                        containerName,
                        typeFilter,
                        formatName,
                        isParallel,
                        includeAssociatedData,
                        maxCount,
                        anonymizationConfigCollectionReference,
                        anonymizationConfigLocation,
                        anonymizationConfigFileETag),
                ExportJobType.All,
                KnownResourceTypes.Patient,
                null,
                "_history,_deleted");
        }

        [Fact]
        public async Task GivenAnExportResourceTypeRequest_WhenProcessing_ThenCreateExportRequestShouldBeCreatedCorrectly()
        {
            await RunCreateExportRequestTest(
                (since, till, resourceType, groupId, containerName, typeFilter, formatName, isParallel, includeAssociatedData, maxCount, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag) =>
                    _exportEnabledController.ExportResourceType(
                        KnownResourceTypes.Patient,
                        since,
                        till,
                        resourceType,
                        containerName,
                        typeFilter,
                        formatName,
                        maxCount,
                        anonymizationConfigCollectionReference,
                        anonymizationConfigLocation,
                        anonymizationConfigFileETag),
                ExportJobType.Patient,
                KnownResourceTypes.Observation);
        }

        [Fact]
        public async Task GivenAnExportResourceTypeByIdRequest_WhenProcessing_ThenCreateExportRequestShouldBeCreatedCorrectly()
        {
            await RunCreateExportRequestTest(
                (since, till, resourceType, groupId, containerName, typeFilter, formatName, isParallel, includeAssociatedData, maxCount, anonymizationConfigCollectionReference, anonymizationConfigLocation, anonymizationConfigFileETag) =>
                    _exportEnabledController.ExportResourceTypeById(
                        KnownResourceTypes.Group,
                        groupId,
                        since,
                        till,
                        resourceType,
                        containerName,
                        typeFilter,
                        formatName,
                        maxCount,
                        anonymizationConfigCollectionReference,
                        anonymizationConfigLocation,
                        anonymizationConfigFileETag),
                ExportJobType.Group,
                KnownResourceTypes.Practitioner,
                Guid.NewGuid().ToString(),
                null,
                false);
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task GivenAGetExportStatusByIdRequest_WhenProcessing_ThenGetExportRequestShouldBeCreatedCorrectly(
            HttpStatusCode statusCode)
        {
            var baseUri = new Uri("https://test.com/");
            _fhirRequestContextAccessor.RequestContext.Uri.Returns(baseUri);

            _mediator
                .Send(Arg.Any<GetExportRequest>(), Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        return new GetExportResponse(
                            statusCode,
                            new ExportJobResult(
                                DateTimeOffset.UtcNow,
                                baseUri,
                                true,
                                new List<ExportOutputResponse>(),
                                new List<ExportOutputResponse>(),
                                new List<OperationOutcomeIssue>()));
                    });

            var request = default(GetExportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<GetExportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x =>
                {
                    request = x.Arg<GetExportRequest>();
                });

            var id = Guid.NewGuid().ToString();
            var response = await _exportEnabledController.GetExportStatusById(id);

            Assert.NotNull(request);
            Assert.Equal(baseUri, request.RequestUri);
            Assert.Equal(id, request.JobId);

            var result = response as ExportResult;
            Assert.NotNull(result);
            Assert.Equal(statusCode == HttpStatusCode.OK ? statusCode : HttpStatusCode.Accepted, result.StatusCode);
        }

        [Fact]
        public async Task GivenACancelExportRequest_WhenProcessing_ThenCancelExportRequestShouldBeCreatedCorrectly()
        {
            var baseUri = new Uri("https://test.com/");
            _fhirRequestContextAccessor.RequestContext.Uri.Returns(baseUri);

            _mediator
                .Send(Arg.Any<CancelExportRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CancelExportResponse(HttpStatusCode.OK));

            var request = default(CancelExportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<CancelExportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x =>
                {
                    request = x.Arg<CancelExportRequest>();
                });

            var id = Guid.NewGuid().ToString();
            var response = await _exportEnabledController.CancelExport(id);

            Assert.NotNull(request);
            Assert.Equal(id, request.JobId);
        }

        private ExportController GetController(ExportJobConfiguration exportConfig, FeatureConfiguration features, ArtifactStoreConfiguration artifactStoreConfig, IFhirRuntimeConfiguration fhirConfig = null)
        {
            var operationConfig = new OperationsConfiguration()
            {
                Export = exportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            IOptions<ArtifactStoreConfiguration> optionsArtifactStoreConfiguration = Substitute.For<IOptions<ArtifactStoreConfiguration>>();
            optionsArtifactStoreConfiguration.Value.Returns(artifactStoreConfig);

            IOptions<FeatureConfiguration> optionsFeatures = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatures.Value.Returns(features);

            return new ExportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                optionsArtifactStoreConfiguration,
                optionsFeatures,
                fhirConfig ?? Substitute.For<IFhirRuntimeConfiguration>());
        }

        private async Task RunCreateExportRequestTest(
            Func<PartialDateTime, PartialDateTime, string, string, string, string, string, bool?, string, uint, string, string, string, Task<IActionResult>> func,
            ExportJobType exportJobType,
            string typeParameter = null,
            string idParameter = null,
            string includeAssociatedDataParameter = null,
            bool isParallel = true)
        {
            var loginServer = "abc.azurecr.io";
            var imageName = "deidconfigs";
            var since = new PartialDateTime(DateTimeOffset.UtcNow.AddSeconds(-60));
            var till = new PartialDateTime(DateTimeOffset.UtcNow);
            var filters = default(string);
            var resourceType = typeParameter;
            var groupId = idParameter;
            var containerName = Guid.NewGuid().ToString();
            var formatName = Guid.NewGuid().ToString();
            var includeAssociatedData = includeAssociatedDataParameter;
            var includeHistory = includeAssociatedDataParameter != null && includeAssociatedDataParameter.Contains("_history");
            var includeDeleted = includeAssociatedDataParameter != null && includeAssociatedDataParameter.Contains("_deleted");
            var maxCount = uint.MaxValue;
            var anonymizationConfigCollectionReference = $"{loginServer}/{imageName}:1ae21c6e33deb90f105982404c867671da624deda7dff364107ec8c2910b4992";
            var anonymizationConfigLocation = Guid.NewGuid().ToString();
            var anonymizationConfigFileETag = default(string);

            _artifactStoreConfig.OciArtifacts.Add(
                new OciArtifactInfo()
                {
                    LoginServer = "abc.azurecr.io",
                    ImageName = "deidconfigs",
                });
            _featureConfiguration.SupportsAnonymizedExport = true;

            // Setup additional dependencies needed for test execution.
            _exportEnabledController.ControllerContext.HttpContext = new DefaultHttpContext();

            var baseUri = new Uri("https://test.com/");
            _fhirRequestContextAccessor.RequestContext = new FhirRequestContext(
               method: "export",
               uriString: baseUri.OriginalString,
               baseUriString: baseUri.OriginalString,
               correlationId: "export",
               requestHeaders: new Dictionary<string, StringValues>(),
               responseHeaders: new Dictionary<string, StringValues>());

            _urlResolver
                .ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>())
                .Returns(baseUri);

            // Mock mediator call for CreateExportRequest - throw exception to fail test if we get unexpected value.
            _mediator
                .Send(Arg.Any<CreateExportRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CreateExportResponse("ExportTestJobId"));

            var request = default(CreateExportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<CreateExportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x =>
                {
                    request = x.Arg<CreateExportRequest>();
                });

            await func(
                since,
                till,
                resourceType,
                groupId,
                containerName,
                filters,
                formatName,
                isParallel,
                includeAssociatedData,
                maxCount,
                anonymizationConfigCollectionReference,
                anonymizationConfigLocation,
                anonymizationConfigFileETag);

            Assert.NotNull(request);
            Assert.Equal(exportJobType, request.RequestType);
            Assert.Equal(baseUri, request.RequestUri);
            Assert.Equal(resourceType, request.ResourceType);
            Assert.Equal(since.ToString(), request.Since?.ToString());
            Assert.Equal(till.ToString(), request.Till?.ToString());
            Assert.Equal(filters, request.Filters);
            Assert.Equal(groupId, request.GroupId);
            Assert.Equal(containerName, request.ContainerName);
            Assert.Equal(formatName, request.FormatName);
            Assert.Equal(isParallel, request.IsParallel);
            Assert.Equal(includeHistory, request.IncludeHistory);
            Assert.Equal(includeDeleted, request.IncludeDeleted);
            Assert.Equal(maxCount, request.MaxCount);
            Assert.Equal(anonymizationConfigCollectionReference, request.AnonymizationConfigurationCollectionReference);
            Assert.Equal(anonymizationConfigLocation, request.AnonymizationConfigurationLocation);
            Assert.Equal(anonymizationConfigFileETag, request.AnonymizationConfigurationFileETag);
        }
    }
}

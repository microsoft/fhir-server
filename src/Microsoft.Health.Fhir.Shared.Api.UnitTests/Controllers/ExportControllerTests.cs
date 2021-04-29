// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ExportControllerTests
    {
        private ExportController _exportEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();

        public ExportControllerTests()
        {
            _exportEnabledController = GetController(new ExportJobConfiguration() { Enabled = true });
        }

        [Fact]
        public async Task GivenAnExportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.Export(
                typeFilter: null,
                since: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null));
        }

        [Fact]
        public async Task GivenAnExportByResourceTypeRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceType(
                typeFilter: null,
                since: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString()));
        }

        [Fact]
        public async Task GivenAnExportByIdRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var exportController = GetController(new ExportJobConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => exportController.ExportResourceTypeById(
                typeFilter: null,
                since: null,
                resourceType: null,
                containerName: null,
                formatName: null,
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
                resourceType: null,
                containerName: null,
                formatName: null,
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
                resourceType: null,
                containerName: null,
                formatName: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString(),
                idParameter: "id"));
        }

        [Fact]
        public async Task GivenAnAnonymizedExportRequest_WhenNoContainerName_ThenRequestNotValidExceptionShouldBeThrown()
        {
            string anonymizationConfig = "anonymizationConfig";

            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.ExportResourceTypeById(
                typeFilter: null,
                since: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                anonymizationConfigLocation: anonymizationConfig,
                anonymizationConfigFileETag: null,
                typeParameter: ResourceType.Patient.ToString(),
                idParameter: "id"));
        }

        [Fact]
        public async Task GivenAnExportRequestWithAnonymizationConfigEtag_WhenNoAnonymizationConfig_ThenRequestNotValidExceptionShouldBeThrown()
        {
            string anonymizationConfigEtag = "anonymizationConfigEtag";

            await Assert.ThrowsAsync<RequestNotValidException>(() => _exportEnabledController.ExportResourceTypeById(
                typeFilter: null,
                since: null,
                resourceType: null,
                containerName: null,
                formatName: null,
                anonymizationConfigLocation: null,
                anonymizationConfigFileETag: anonymizationConfigEtag,
                typeParameter: ResourceType.Patient.ToString(),
                idParameter: "id"));
        }

        private ExportController GetController(ExportJobConfiguration exportConfig)
        {
            var operationConfig = new OperationsConfiguration()
            {
                Export = exportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            var features = new FeatureConfiguration();
            IOptions<FeatureConfiguration> optionsFeatures = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatures.Value.Returns(features);

            return new ExportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                optionsFeatures,
                NullLogger<ExportController>.Instance);
        }
    }
}

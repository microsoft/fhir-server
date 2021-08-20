﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Import;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class ImportControllerTests
    {
        private IMediator _mediator = Substitute.For<IMediator>();
        private RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();

        public static TheoryData<ImportRequest> ValidBody =>
            new TheoryData<ImportRequest>
            {
                GetValidBulkImportRequestConfiguration(),
            };

        public static TheoryData<ImportRequest> InValidBody =>
            new TheoryData<ImportRequest>
            {
                GetBulkImportRequestConfigurationWithUnsupportedInputFormat(),
                GetBulkImportRequestConfigurationWithUnsupportedStorageType(),
                GetBulkImportRequestConfigurationWithUnsupportedResourceType(),
                GetBulkImportRequestConfigurationWithNoInputFile(),
            };

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(ImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown(ImportRequest body)
        {
            var bulkImportController = GetController(new ImportTaskConfiguration() { Enabled = false });

            body.Mode = ImportConstants.InitialLoadMode;
            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(body.ToParameters()));
        }

        [Theory]
        [MemberData(nameof(InValidBody), MemberType = typeof(ImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenRequestConfigurationNotValid_ThenRequestNotValidExceptionShouldBeThrown(ImportRequest body)
        {
            var bulkImportController = GetController(new ImportTaskConfiguration() { Enabled = true });

            body.Mode = ImportConstants.InitialLoadMode;
            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(body.ToParameters()));
        }

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(ImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenRequestWithoutMode_ThenRequestNotValidExceptionShouldBeThrown(ImportRequest body)
        {
            var bulkImportController = GetController(new ImportTaskConfiguration() { Enabled = true });

            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(body.ToParameters()));
        }

        private static CreateImportResponse CreateBulkImportResponse()
        {
            return new CreateImportResponse("123");
        }

        private ImportController GetController(ImportTaskConfiguration bulkImportConfig)
        {
            var operationConfig = new OperationsConfiguration()
            {
                Import = bulkImportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            var features = new FeatureConfiguration();
            IOptions<FeatureConfiguration> optionsFeatures = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatures.Value.Returns(features);

            return new ImportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                optionsFeatures,
                NullLogger<ImportController>.Instance);
        }

        private static ImportRequest GetValidBulkImportRequestConfiguration()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
                new InputResource
                {
                    Type = "Observation",
                    Url = new Uri("https://client.example.org/obseration_file_19.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var importRequest = new ImportRequest();
            importRequest.InputFormat = "application/fhir+ndjson";
            importRequest.InputSource = new Uri("https://other-server.example.org");
            importRequest.Input = input;
            importRequest.StorageDetail = new ImportRequestStorageDetail();

            return importRequest;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithUnsupportedInputFormat()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/json";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithUnsupportedStorageType()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new ImportRequestStorageDetail
            {
                Type = "Fake",
            };

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithUnsupportedResourceType()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Fake",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithNoInputFile()
        {
            var input = new List<InputResource>();

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Operations.BulkImport.Models;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class BulkImportControllerTests
    {
        private BulkImportController _bulkImportEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();

        public BulkImportControllerTests()
        {
            _bulkImportEnabledController = GetController(new BulkImportTaskConfiguration() { Enabled = true });
        }

        public static TheoryData<BulkImportRequest> ValidBody =>
            new TheoryData<BulkImportRequest>
            {
                GetValidBulkImportRequestConfiguration(),
            };

        public static TheoryData<BulkImportRequest> InValidBody =>
            new TheoryData<BulkImportRequest>
            {
                GetBulkImportRequestConfigurationWithUnsupportedInputFormat(),
                GetBulkImportRequestConfigurationWithUnsupportedStorageType(),
                GetBulkImportRequestConfigurationWithUnsupportedResourceType(),
            };

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(BulkImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown(BulkImportRequest body)
        {
            var bulkImportController = GetController(new BulkImportTaskConfiguration() { Enabled = false });

            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.BulkImport(body));
        }

        [Theory]
        [MemberData(nameof(InValidBody), MemberType = typeof(BulkImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenRequestConfigurationNotValid_ThenRequestNotValidExceptionShouldBeThrown(BulkImportRequest body)
        {
            var bulkImportController = GetController(new BulkImportTaskConfiguration() { Enabled = true });

            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.BulkImport(body));
        }

        private static CreateBulkImportResponse CreateBulkImportResponse()
        {
            return new CreateBulkImportResponse("123");
        }

        private BulkImportController GetController(BulkImportTaskConfiguration bulkImportConfig)
        {
            var operationConfig = new OperationsConfiguration()
            {
                BulkImport = bulkImportConfig,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new BulkImportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                NullLogger<BulkImportController>.Instance);
        }

        private static BulkImportRequest GetValidBulkImportRequestConfiguration()
        {
            var input = new List<BulkImportRequestInput>
            {
                new BulkImportRequestInput
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
                new BulkImportRequestInput
                {
                    Type = "Observation",
                    Url = new Uri("https://client.example.org/obseration_file_19.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new BulkImportRequestStorageDetail();

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequest GetBulkImportRequestConfigurationWithUnsupportedInputFormat()
        {
            var input = new List<BulkImportRequestInput>
            {
                new BulkImportRequestInput
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/json";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequest GetBulkImportRequestConfigurationWithUnsupportedStorageType()
        {
            var input = new List<BulkImportRequestInput>
            {
                new BulkImportRequestInput
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new BulkImportRequestStorageDetail
            {
                Type = "Fake",
            };

            return bulkImportRequestConfiguration;
        }

        private static BulkImportRequest GetBulkImportRequestConfigurationWithUnsupportedResourceType()
        {
            var input = new List<BulkImportRequestInput>
            {
                new BulkImportRequestInput
                {
                    Type = "Fake",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new BulkImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }
    }
}

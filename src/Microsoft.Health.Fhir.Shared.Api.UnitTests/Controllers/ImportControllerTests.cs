// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
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

        public static TheoryData<ImportRequest> InvalidBody =>
            new TheoryData<ImportRequest>
            {
                GetBulkImportRequestConfigurationWithUnsupportedInputFormat(),
                GetBulkImportRequestConfigurationWithUnsupportedStorageType(),
                GetBulkImportRequestConfigurationWithUnsupportedResourceType(),
                GetBulkImportRequestConfigurationWithSearchParameterResourceType(),
                GetBulkImportRequestConfigurationWithNoInputFile(),
                GetBulkImportRequestConfigurationWithNoInputUrl(),
                GetBulkImportRequestConfigurationWithSASToken(),
                GetBulkImportRequestConfigurationWithRelativeInputUrl(),
            };

        [Theory]
        [MemberData(nameof(ValidBody), MemberType = typeof(ImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenDisabled_ThenRequestNotValidExceptionShouldBeThrown(ImportRequest body)
        {
            var bulkImportController = GetController(new ImportJobConfiguration() { Enabled = false });

            body.Mode = ImportMode.InitialLoad.ToString();
            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(body.ToParameters()));
        }

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(ImportControllerTests))]
        public async Task GivenAnBulkImportRequest_WhenRequestConfigurationNotValid_ThenRequestNotValidExceptionShouldBeThrown(ImportRequest body)
        {
            var bulkImportController = GetController(new ImportJobConfiguration() { Enabled = true });

            body.Mode = ImportMode.InitialLoad.ToString();
            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(body.ToParameters()));
        }

        [Fact]
        public async Task GivenAnBulkImportRequest_WhenRequestWithNullParameters_ThenRequestNotValidExceptionShouldBeThrown()
        {
            Parameters parameters = null;
            var bulkImportController = GetController(new ImportJobConfiguration() { Enabled = true });
            await Assert.ThrowsAsync<RequestNotValidException>(() => bulkImportController.Import(parameters));
        }

        [Fact]
        public async Task GivenAnBulkImportRequest_WhenRequestWithDuplicateFiles_ThenRequestNotValidExceptionShouldBeThrown()
        {
            var requestWithDuplicateUrls = GetDuplicateFileImportRequest();
            var bulkImportController = GetController(new ImportJobConfiguration() { Enabled = true });

            var controllerException = await Assert.ThrowsAsync<RequestNotValidException>(
                () => bulkImportController.Import(requestWithDuplicateUrls.ToParameters()));

            Assert.Contains(requestWithDuplicateUrls.Input[0].Url.ToString(), controllerException.Message);
        }

        [Theory]
        [InlineData("IncrementalLoad", false, false)]
        [InlineData("InitialLoad", false, false)]
        [InlineData("UnknownLoad", false, false)]
        public async Task GivenAnImportRequest_WhenProcessing_ThenCreateImportRequestShouldBeCreatedCorrectly(
            string mode,
            bool force,
            bool initialImportMode)
        {
            var baseUri = new Uri("https://test.com/");
            _fhirRequestContextAccessor.RequestContext.Uri.Returns(baseUri);
            _urlResolver
                .ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>())
                .Returns(baseUri);

            var importRequest = GetValidBulkImportRequestConfiguration();
            importRequest.Mode = mode;
            importRequest.Force = force;
            importRequest.ErrorContainerName = "errors";
            importRequest.EventualConsistency = true;
            importRequest.ProcessingUnitBytesToRead = int.MaxValue;

            var id = Guid.NewGuid().ToString();
            _mediator.Send(Arg.Any<CreateImportRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CreateImportResponse(id));

            var request = default(CreateImportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<CreateImportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(callInfo =>
                {
                    request = callInfo.ArgAt<CreateImportRequest>(0);
                });

            var expectedMode = Enum.TryParse<ImportMode>(mode, ignoreCase: true, out var x)
                ? x
                : ImportMode.InitialLoad;
            var valid = string.IsNullOrEmpty(mode)
                || (Enum.TryParse<ImportMode>(mode, ignoreCase: true, out x)
                && !(x == ImportMode.InitialLoad && !force && !initialImportMode));
            try
            {
                var controller = GetController(
                    new ImportJobConfiguration()
                    {
                        Enabled = true,
                        InitialImportMode = initialImportMode,
                    });
                var response = await controller.Import(importRequest.ToParameters());
                Assert.True(valid);
                Assert.NotNull(response);

                var result = response as ImportResult;
                Assert.NotNull(result);
                Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);

                Assert.NotNull(request);
                Assert.Equal(_fhirRequestContextAccessor.RequestContext.Uri, request.RequestUri);
                Assert.Equal(importRequest.InputFormat, request.InputFormat);
                Assert.Equal(importRequest.InputSource, request.InputSource);
                Assert.Equal(mode, request.ImportMode.ToString(), StringComparer.OrdinalIgnoreCase);
                Assert.Equal(importRequest.ErrorContainerName, request.ErrorContainerName);
                Assert.Equal(importRequest.EventualConsistency, request.EventualConsistency);
                Assert.Equal(importRequest.ProcessingUnitBytesToRead, request.ProcessingUnitBytesToRead);
                Assert.Equal(importRequest.Input.Count, request.Input.Count);
                Assert.All(
                    importRequest.Input,
                    x =>
                    {
                        Assert.Contains(
                            request.Input,
                            y =>
                            {
                                return string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase) && x.Url == y.Url;
                            });
                    });
            }
            catch (RequestNotValidException)
            {
                Assert.False(valid);
            }
        }

        [Fact]
        public async Task GivenACancelImportRequest_WhenProcessing_ThenCancelImportRequestShouldBeCreatedCorrectly()
        {
            _mediator
                .Send(Arg.Any<CancelImportRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CancelImportResponse(HttpStatusCode.OK));

            var request = default(CancelImportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<CancelImportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x =>
                {
                    request = x.Arg<CancelImportRequest>();
                });

            var controller = GetController(
                new ImportJobConfiguration()
                {
                    Enabled = true,
                });

            var id = int.MaxValue;
            var response = await controller.CancelImport(id);

            Assert.NotNull(request);
            Assert.Equal(id, request.JobId);
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, true)]
        [InlineData(HttpStatusCode.BadRequest, false)]
        [InlineData(HttpStatusCode.InternalServerError, true)]
        public async Task GivenAGetImportStatusByIdRequest_WhenProcessing_ThenGetImportRequestShouldBeCreatedCorrectly(
            HttpStatusCode statusCode,
            bool returnDetails)
        {
            var baseUri = new Uri("https://test.com/");
            _fhirRequestContextAccessor.RequestContext.Uri.Returns(baseUri);
            _urlResolver
                .ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>())
                .Returns(baseUri);

            _mediator
                .Send(Arg.Any<GetImportRequest>(), Arg.Any<CancellationToken>())
                .Returns(
                    x =>
                    {
                        if (returnDetails)
                        {
                            return new GetImportResponse(
                                statusCode,
                                new ImportJobResult()
                                {
                                    Request = baseUri.OriginalString,
                                    TransactionTime = DateTimeOffset.UtcNow,
                                    Output = new List<ImportOperationOutcome>(),
                                    Error = new List<ImportFailedOperationOutcome>(),
                                });
                        }

                        return new GetImportResponse(statusCode);
                    });

            var request = default(GetImportRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<GetImportRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x =>
                {
                    request = x.Arg<GetImportRequest>();
                });

            var controller = GetController(
                new ImportJobConfiguration()
                {
                    Enabled = true,
                });

            var id = int.MaxValue;
            var response = await controller.GetImportStatusById(id, false);

            Assert.NotNull(request);
            Assert.Equal(id, request.JobId);
            Assert.False(request.ReturnDetails);

            var result = response as ImportResult;
            Assert.NotNull(result);
            Assert.Equal(statusCode == HttpStatusCode.OK ? statusCode : HttpStatusCode.Accepted, result.StatusCode);
            Assert.Equal(returnDetails, result.Result != null);
        }

        private ImportController GetController(ImportJobConfiguration bulkImportConfig)
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

            var controller = new ImportController(
                _mediator,
                _fhirRequestContextAccessor,
                _urlResolver,
                optionsOperationConfiguration,
                optionsFeatures,
                NullLogger<ImportController>.Instance);
            controller.ControllerContext = new ControllerContext(
               new ActionContext(
                   Substitute.For<HttpContext>(),
                   new RouteData(),
                   new ControllerActionDescriptor()));
            return controller;
        }

        private static ImportRequest GetValidBulkImportRequestConfiguration()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
                },
                new InputResource
                {
                    Type = "Observation",
                    Url = new Uri("https://client.example.org/obseration_file_19.ndjson"),
                },
            };

            var importRequest = new ImportRequest();
            importRequest.InputFormat = "application/fhir+ndjson";
            importRequest.InputSource = new Uri("https://other-server.example.org");
            importRequest.Input = input;
            importRequest.StorageDetail = new ImportRequestStorageDetail();

            return importRequest;
        }

        private static ImportRequest GetDuplicateFileImportRequest()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
                },
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
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
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
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
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
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
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithSearchParameterResourceType()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "SearchParameter",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson"),
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

        private static ImportRequest GetBulkImportRequestConfigurationWithNoInputUrl()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = null,
                },
            };
            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithSASToken()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("https://client.example.org/patient_file_2.ndjson?sp=r&st=2022-09-30T01:39:01Z&se=2022-09-30T09:39:01Z&spr=https&sv=2021-06-08&sr=b&sig=RHIX5Xcg0Mq2rqI3OlWT"),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new ImportRequestStorageDetail();

            return bulkImportRequestConfiguration;
        }

        private static ImportRequest GetBulkImportRequestConfigurationWithRelativeInputUrl()
        {
            var input = new List<InputResource>
            {
                new InputResource
                {
                    Type = "Patient",
                    Url = new Uri("/blob/patient_file_2.ndjson", UriKind.RelativeOrAbsolute),
                },
            };

            var bulkImportRequestConfiguration = new ImportRequest();
            bulkImportRequestConfiguration.InputFormat = "application/fhir+ndjson";
            bulkImportRequestConfiguration.InputSource = new Uri("https://other-server.example.org");
            bulkImportRequestConfiguration.Input = input;
            bulkImportRequestConfiguration.StorageDetail = new ImportRequestStorageDetail();

            return bulkImportRequestConfiguration;
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.ArtifactStore;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ConvertDataControllerTests
    {
        private ConvertDataController _convertDataEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private static ConvertDataConfiguration _convertDataJobConfig = new ConvertDataConfiguration() { Enabled = true };
        private static ArtifactStoreConfiguration _artifactStoreConfig = new ArtifactStoreConfiguration();

        private const string _testImageReference = "test.azurecr.io/testimage:latest";
        private const string _testHl7v2RootTemplate = "ADT_A01";
        private const string _testCcdaRootTemplate = "CCD";
        private const string _testJsonRootTemplate = "ExamplePatient";
        private const string _testFhirStu3ToR4RootTemplate = "Patient";

        public ConvertDataControllerTests()
        {
            _convertDataJobConfig.ContainerRegistryServers.Add("test.azurecr.io");
            _convertDataEnabledController = GetController(_convertDataJobConfig, _artifactStoreConfig);
        }

        public static TheoryData<Parameters> InvalidBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithTooManyParams(),
                GetParamsResourceWithMissingParams(),
                GetParamsResourceWithWrongNameParam(),
                GetParamsResourceWithUnsupportedDataType(),
                null,
            };

        public static TheoryData<Parameters> InconsistentBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithInconsistentParamsWrongDataType(),
                GetParamsResourceWithInconsistentParamsWrongDefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongHl7v2DefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongCcdaDefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongJsonDefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongFhirDefaultTemplates(),
            };

        public static TheoryData<Parameters> Hl7v2ValidBody =>
            new TheoryData<Parameters>
            {
                GetHl7v2ValidConvertDataParams(),
                GetHl7v2ValidConvertDataParamsIgnoreCasesAllLowercase(),
                GetHl7v2ValidConvertDataParamsIgnoreCasesAllUppercase(),
            };

        public static TheoryData<Parameters> CcdaValidBody =>
            new TheoryData<Parameters>
            {
                GetCcdaValidConvertDataParams(),
                GetCcdaValidConvertDataParamsIgnoreCasesAllLowercase(),
                GetCcdaValidConvertDataParamsIgnoreCasesAllUppercase(),
            };

        public static TheoryData<Parameters> JsonValidBody =>
            new TheoryData<Parameters>
            {
                GetJsonValidConvertDataParams(),
                GetJsonValidConvertDataParamsIgnoreCasesAllLowercase(),
                GetJsonValidConvertDataParamsIgnoreCasesAllUppercase(),
            };

        public static TheoryData<Parameters> FhirValidBody =>
            new TheoryData<Parameters>
            {
                GetFhirValidConvertDataParams(),
                GetFhirValidConvertDataParamsIgnoreCasesAllLowercase(),
                GetFhirValidConvertDataParamsIgnoreCasesAllUppercase(),
            };

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WhenInvalidBodySent_ThenRequestNotValidThrown(Parameters body)
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataEnabledController.ConvertData(body));
        }

        [Theory]
        [MemberData(nameof(InconsistentBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WhenInconsistentBodySent_ThenInconsistentThrown(Parameters body)
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataEnabledController.ConvertData(body));
        }

        [Theory]
        [InlineData("abc.azurecr.io")]
        [InlineData("abc.azurecr.io/:tag")]
        [InlineData("testimage:tag")]
        public async Task GivenAConvertDataRequest_WithInvalidReference_WhenInvalidBodySent_ThenRequestNotValidThrown(string templateCollectionReference)
        {
            var body = GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", templateCollectionReference, _testHl7v2RootTemplate);

            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataEnabledController.ConvertData(body));
        }

        [Theory]
        [InlineData(null, null, null, "abc.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", null, null, "dummy.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", "template", null, "abc.azurecr.io/convertertemplate:123")]
        [InlineData("abc.azurecr.io", "template", "sha256:123abc", "abc.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", "template", "sha256:123abc", "abc.azurecr.io/template1@sha256:123")]
        [InlineData("abc.azurecr.io", "template", "sha256:123abc", "abc.azurecr.io/template@sha256:123abd")]
        public async Task GivenAConvertDataRequest_WithNotConfiguredTemplate_WhenValidBodySent_ThenTemplateNotConfiguredThrown(string loginServer, string imageName, string digest, string templateCollectionReference)
        {
            var ociArtifactInfo = new OciArtifactInfo
            {
                LoginServer = loginServer,
                ImageName = imageName,
                Digest = digest,
            };
            var artifactConfig = new ArtifactStoreConfiguration();
            artifactConfig.OciArtifacts.Add(ociArtifactInfo);

            var localController = GetController(_convertDataJobConfig, artifactConfig);
            var body = GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", templateCollectionReference, _testHl7v2RootTemplate);

            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => localController.ConvertData(body));
        }

        [Theory]
        [InlineData(null, null, null, "test.azurecr.io/template:123")] // configured in ConvertData config
        [InlineData("abc.azurecr.io", null, null, "abc.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", "template", null, "abc.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", "template", null, "ABC.azurecr.io/template:123")]
        [InlineData("abc.azurecr.io", "template", "sha256:123abc", "abc.azurecr.io/template@sha256:123abc")]
        public async Task GivenAConvertDataRequest_WithConfiguredTemplate_WhenValidBodySent_ThenConvertDataCalledWithCorrectParams(string loginServer, string imageName, string digest, string templateCollectionReference)
        {
            _mediator.Send(Arg.Any<ConvertDataRequest>()).Returns(Task.FromResult(GetConvertDataResponse()));

            var ociArtifactInfo = new OciArtifactInfo
            {
                LoginServer = loginServer,
                ImageName = imageName,
                Digest = digest,
            };
            var artifactConfig = new ArtifactStoreConfiguration();
            artifactConfig.OciArtifacts.Add(ociArtifactInfo);

            var localController = GetController(_convertDataJobConfig, artifactConfig);
            var body = GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", templateCollectionReference, _testHl7v2RootTemplate);

            await localController.ConvertData(body);
            await _mediator.Received().Send(
                Arg.Is<ConvertDataRequest>(
                     r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputData)).Value.ToString())
                && string.Equals(r.InputDataType.ToString(), body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputDataType)).Value.ToString(), StringComparison.OrdinalIgnoreCase)
                && r.TemplateCollectionReference == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.TemplateCollectionReference)).Value.ToString()
                && r.RootTemplate == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.RootTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        [Theory]
        [MemberData(nameof(Hl7v2ValidBody), MemberType = typeof(ConvertDataControllerTests))]
        [MemberData(nameof(CcdaValidBody), MemberType = typeof(ConvertDataControllerTests))]
        [MemberData(nameof(JsonValidBody), MemberType = typeof(ConvertDataControllerTests))]
        [MemberData(nameof(FhirValidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WithValidBody_ThenConvertDataCalledWithCorrectParams(Parameters body)
        {
            _mediator.Send(Arg.Any<ConvertDataRequest>()).Returns(Task.FromResult(GetConvertDataResponse()));
            await _convertDataEnabledController.ConvertData(body);
            await _mediator.Received().Send(
                Arg.Is<ConvertDataRequest>(
                     r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputData)).Value.ToString())
                && string.Equals(r.InputDataType.ToString(), body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputDataType)).Value.ToString(), StringComparison.OrdinalIgnoreCase)
                && r.TemplateCollectionReference == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.TemplateCollectionReference)).Value.ToString()
                && r.RootTemplate == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.RootTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private static ConvertDataResponse GetConvertDataResponse() => new ConvertDataResponse(Samples.SampleConvertDataResponse);

        private ConvertDataController GetController(
            ConvertDataConfiguration convertDataConfiguration,
            ArtifactStoreConfiguration artifactStoreConfiguration)
        {
            var operationConfig = new OperationsConfiguration()
            {
                ConvertData = convertDataConfiguration,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            IOptions<ArtifactStoreConfiguration> optionsArtifactStoreConfiguration = Substitute.For<IOptions<ArtifactStoreConfiguration>>();
            optionsArtifactStoreConfiguration.Value.Returns(artifactStoreConfiguration);

            return new ConvertDataController(
                _mediator,
                optionsOperationConfiguration,
                optionsArtifactStoreConfiguration,
                NullLogger<ConvertDataController>.Instance);
        }

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, ConvertDataProperties.InputData, Samples.SampleHl7v2Message);
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithMissingParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, ConvertDataProperties.InputData, Samples.SampleHl7v2Message);
            AddParamComponent(parametersResource, ConvertDataProperties.InputDataType, "Hl7v2");
            AddParamComponent(parametersResource, ConvertDataProperties.TemplateCollectionReference, _testImageReference);

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithTooManyParams()
        {
            var parametersResource = GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", _testImageReference, _testHl7v2RootTemplate);

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithInconsistentParamsWrongDataType()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "Ccda", "microsofthealth/fhirconverter:default", _testHl7v2RootTemplate);

        private static Parameters GetParamsResourceWithInconsistentParamsWrongDefaultTemplates()
        => GetConvertDataParams(Samples.SampleCcdaMessage, "Ccda", "microsofthealth/fhirconverter:default", _testCcdaRootTemplate);

        private static Parameters GetParamsResourceWithInconsistentParamsWrongHl7v2DefaultTemplates()
        => GetConvertDataParams(Samples.SampleCcdaMessage, "Ccda", "microsofthealth/hl7v2templates:default", _testCcdaRootTemplate);

        private static Parameters GetParamsResourceWithInconsistentParamsWrongCcdaDefaultTemplates()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", "microsofthealth/ccdatemplates:default", _testHl7v2RootTemplate);

        private static Parameters GetParamsResourceWithInconsistentParamsWrongJsonDefaultTemplates()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", "microsofthealth/jsontemplates:default", _testHl7v2RootTemplate);

        private static Parameters GetParamsResourceWithInconsistentParamsWrongFhirDefaultTemplates()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", "microsofthealth/stu3tor4templates:default", _testHl7v2RootTemplate);

        private static Parameters GetParamsResourceWithUnsupportedDataType()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "invalid", _testImageReference, _testHl7v2RootTemplate);

        private static Parameters GetHl7v2ValidConvertDataParams()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "Hl7v2", _testImageReference, _testHl7v2RootTemplate);

        private static Parameters GetHl7v2ValidConvertDataParamsIgnoreCasesAllLowercase()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", _testImageReference, _testHl7v2RootTemplate);

        private static Parameters GetHl7v2ValidConvertDataParamsIgnoreCasesAllUppercase()
        => GetConvertDataParams(Samples.SampleHl7v2Message, "HL7V2", _testImageReference, _testHl7v2RootTemplate);

        private static Parameters GetCcdaValidConvertDataParams()
        => GetConvertDataParams(Samples.SampleCcdaMessage, "Ccda", _testImageReference, _testCcdaRootTemplate);

        private static Parameters GetCcdaValidConvertDataParamsIgnoreCasesAllLowercase()
        => GetConvertDataParams(Samples.SampleCcdaMessage, "ccda", _testImageReference, _testCcdaRootTemplate);

        private static Parameters GetCcdaValidConvertDataParamsIgnoreCasesAllUppercase()
        => GetConvertDataParams(Samples.SampleCcdaMessage, "CCDA", _testImageReference, _testCcdaRootTemplate);

        private static Parameters GetJsonValidConvertDataParams()
        => GetConvertDataParams(Samples.SampleJsonMessage, "Json", _testImageReference, _testJsonRootTemplate);

        private static Parameters GetJsonValidConvertDataParamsIgnoreCasesAllLowercase()
        => GetConvertDataParams(Samples.SampleJsonMessage, "json", _testImageReference, _testJsonRootTemplate);

        private static Parameters GetJsonValidConvertDataParamsIgnoreCasesAllUppercase()
        => GetConvertDataParams(Samples.SampleJsonMessage, "JSON", _testImageReference, _testJsonRootTemplate);

        private static Parameters GetFhirValidConvertDataParams()
        => GetConvertDataParams(Samples.SampleFhirStu3Message, "Fhir", _testImageReference, _testFhirStu3ToR4RootTemplate);

        private static Parameters GetFhirValidConvertDataParamsIgnoreCasesAllLowercase()
        => GetConvertDataParams(Samples.SampleFhirStu3Message, "fhir", _testImageReference, _testFhirStu3ToR4RootTemplate);

        private static Parameters GetFhirValidConvertDataParamsIgnoreCasesAllUppercase()
        => GetConvertDataParams(Samples.SampleFhirStu3Message, "FHIR", _testImageReference, _testFhirStu3ToR4RootTemplate);

        private static Parameters GetConvertDataParams(string inputData, string inputDataType, string templateSetReference, string rootTemplate)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            AddParamComponent(parametersResource, ConvertDataProperties.InputData, inputData);
            AddParamComponent(parametersResource, ConvertDataProperties.InputDataType, inputDataType);
            AddParamComponent(parametersResource, ConvertDataProperties.TemplateCollectionReference, templateSetReference);
            AddParamComponent(parametersResource, ConvertDataProperties.RootTemplate, rootTemplate);

            return parametersResource;
        }

        private static void AddParamComponent(Parameters resource, string name, string value) =>
            resource.Parameter.Add(new Parameters.ParameterComponent() { Name = name, Value = new FhirString(value) });
    }
}

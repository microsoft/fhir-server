// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [Trait(Traits.Category, Categories.Xml)]
    public class FhirXmlInputFormatterTests
    {
        [Fact]
        public void GivenAFhirModelAndFhirContentType_WhenCheckingCanReadType_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), ContentType.XML_CONTENT_HEADER);

            Assert.True(result);
        }

        [Fact]
        public void GivenAFhirModelAndXmlContentType_WhenCheckingContentTypeFallback_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), "application/myspecialjson+xml");

            Assert.True(result);
        }

        [Fact]
        public void GivenAFhirModelAndJsonContentType_WhenCheckingCanReadType_ThenFalseShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), ContentType.JSON_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public void GivenAXmlElementAndXmlContentType_WhenCheckingCanReadType_ThenFalseShouldBeReturned()
        {
            bool result = CanRead(typeof(XmlElement), ContentType.XML_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public async Task GivenAnInvalidModel_WhenParsing_ThenAnErrorShouldBeAddedToModelState()
        {
            var modelStateDictionary = new ModelStateDictionary();

            var result = await ReadRequestBody(Samples.GetXml("ObservationWithInvalidStatus"), modelStateDictionary);

            Assert.False(result.IsModelSet);
            Assert.Equal(1, modelStateDictionary.ErrorCount);
        }

        [Fact]
        public async Task GivenAModelWithValidationErrors_WhenParsing_ThenTheModelShouldBeReturned()
        {
            var modelStateDictionary = new ModelStateDictionary();

            var result = await ReadRequestBody(Samples.GetXml("ObservationWithNoCode"), modelStateDictionary);

            Assert.True(result.IsModelSet);

            // Model validation error != an Invalid model
            // Model validation is not done at this stage in the pipeline
            // Therefore we should not have attribute validation errors yet
            Assert.Equal(0, modelStateDictionary.ErrorCount);
        }

        [Fact]
        public async Task GivenAnEmptyValue_WhenParsing_ThenNoModelIsReturned()
        {
            var modelStateDictionary = new ModelStateDictionary();

            var result = await ReadRequestBody("  ", modelStateDictionary);

            Assert.False(result.IsModelSet);
            Assert.Equal(1, modelStateDictionary.ErrorCount);
        }

        private static async Task<InputFormatterResult> ReadRequestBody(string sampleXml, ModelStateDictionary modelStateDictionary)
        {
            var formatter = new FhirXmlInputFormatter(new FhirXmlParser());

            var metaData = new DefaultModelMetadata(
                new EmptyModelMetadataProvider(),
                Substitute.For<ICompositeMetadataDetailsProvider>(),
                new DefaultMetadataDetails(ModelMetadataIdentity.ForType(typeof(Observation)), ModelAttributes.GetAttributesForType(typeof(Observation))));
            var defaultHttpContext = new DefaultHttpContext();

            defaultHttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(sampleXml));

            var context = new InputFormatterContext(
                defaultHttpContext,
                KnownActionParameterNames.Resource,
                modelStateDictionary,
                metaData,
                Substitute.For<Func<Stream, Encoding, TextReader>>());

            return await formatter.ReadRequestBodyAsync(context);
        }

        private bool CanRead(Type modelType, string contentType)
        {
            var formatter = new FhirXmlInputFormatter(new FhirXmlParser());
            var modelMetadata = Substitute.For<ModelMetadata>(ModelMetadataIdentity.ForType(modelType));
            var defaultHttpContext = new DefaultHttpContext();
            defaultHttpContext.Request.ContentType = contentType;

            var result = formatter.CanRead(
                new InputFormatterContext(
                    defaultHttpContext,
                    "model",
                    new ModelStateDictionary(),
                    modelMetadata,
                    Substitute.For<Func<Stream, Encoding, TextReader>>()));
            return result;
        }
    }
}

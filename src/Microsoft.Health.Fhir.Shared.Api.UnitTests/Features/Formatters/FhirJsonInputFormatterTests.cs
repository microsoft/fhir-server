// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirJsonInputFormatterTests
    {
        [Fact]
        public void GivenAFhirModelAndFhirContentType_WhenCheckingCanReadType_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), ContentType.JSON_CONTENT_HEADER);

            Assert.True(result);
        }

        [Fact]
        public void GivenAFhirModelAndXJsonContentType_WhenCheckingContentTypeFallback_ThenTrueShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), "application/myspecialjson+json");

            Assert.True(result);
        }

        [Fact]
        public void GivenAFhirModelAndXmlContentType_WhenCheckingCanReadType_ThenFalseShouldBeReturned()
        {
            bool result = CanRead(typeof(Resource), ContentType.XML_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public void GivenAJObjectAndJsonContentType_WhenCheckingCanReadType_ThenFalseShouldBeReturned()
        {
            bool result = CanRead(typeof(JObject), ContentType.JSON_CONTENT_HEADER);

            Assert.False(result);
        }

        [Fact]
        public async Task GivenAnInvalidModel_WhenParsing_ThenAnErrorShouldBeAddedToModelState()
        {
            var modelStateDictionary = new ModelStateDictionary();

            var result = await ReadRequestBody(Samples.GetJson("ObservationWithInvalidStatus"), modelStateDictionary, false);

            Assert.False(result.IsModelSet);
            Assert.Equal(1, modelStateDictionary.ErrorCount);
        }

        [Fact]
        public async Task GivenAModelWithValidationErrors_WhenParsing_ThenTheModelShouldBeReturned()
        {
            var modelStateDictionary = new ModelStateDictionary();

            var result = await ReadRequestBody(Samples.GetJson("ObservationWithNoCode"), modelStateDictionary, false);

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

            var result = await ReadRequestBody("  ", modelStateDictionary, false);

            Assert.False(result.IsModelSet);
            Assert.Equal(1, modelStateDictionary.ErrorCount);
        }

        [Fact]
        public async Task GivenAResourceWithMissingResourceType_WhenParsing_ThenAnErrorShouldBeAddedToModelState()
        {
            var modelStateDictionary = new ModelStateDictionary();
            var patient = Samples.GetJson("PatientMissingResourceType");
            var result = await ReadRequestBody(patient, modelStateDictionary, false);

            Assert.False(result.IsModelSet);
            Assert.Equal(1, modelStateDictionary.ErrorCount);

            (_, ModelStateEntry entry) = modelStateDictionary.First();

            Assert.Single(entry.Errors);
            try
            {
                new FhirJsonParser().Parse<Resource>(patient);
            }
            catch (Exception ex)
            {
                Assert.Equal(string.Format(Api.Resources.ParsingError, ex.Message), entry.Errors.First().ErrorMessage);
            }
        }

        [Fact]
        public async Task GivenAResourceWithUnknownElements_WhenParsing_ThenFirstErrorShouldBeAddedToModelState()
        {
            var modelStateDictionary = new ModelStateDictionary();
            var patient = Samples.GetJson("PatientWithUnknownElements");
            var result = await ReadRequestBody(patient, modelStateDictionary, false);

            Assert.Equal(1, modelStateDictionary.ErrorCount);

            (_, ModelStateEntry entry) = modelStateDictionary.First();

            Assert.Single(entry.Errors);
            try
            {
                new FhirJsonParser().Parse<Resource>(patient);
            }
            catch (Exception ex)
            {
                Assert.Equal(string.Format(Api.Resources.ParsingError, ex.Message), entry.Errors.First().ErrorMessage);
            }
        }

        [Fact]
        public async Task GivenABundleWithUnknownElementsAndInvalidEnums_WhenParsing_ThenAllUnknownElementErrorShouldBeAddedToModelState()
        {
            var modelStateDictionary = new ModelStateDictionary();
            var bundle = Samples.GetJson("Bundle-BatchWithInvalidEleEnums");
            var result = await ReadRequestBody(bundle, modelStateDictionary, true);

            Assert.Equal(4, modelStateDictionary.ErrorCount);
        }

        private static async Task<InputFormatterResult> ReadRequestBody(string sampleJson, ModelStateDictionary modelStateDictionary, bool isBundle)
        {
            var formatter = new FhirJsonInputFormatter(new FhirJsonParser(), ArrayPool<char>.Shared);

            var metaData = new DefaultModelMetadata(
                new EmptyModelMetadataProvider(),
                Substitute.For<ICompositeMetadataDetailsProvider>(),
                new DefaultMetadataDetails(ModelMetadataIdentity.ForType(typeof(Observation)), ModelAttributes.GetAttributesForType(typeof(Observation))));
            var context = new InputFormatterContext(
                new DefaultHttpContext(),
                KnownActionParameterNames.Resource,
                modelStateDictionary,
                metaData,
                (stream, encoding) => new StreamReader(new MemoryStream(encoding.GetBytes(sampleJson))));

            if (isBundle)
            {
                context.HttpContext.Request.Method = "POST";
                context.HttpContext.Request.Path = "/";
            }

            return await formatter.ReadRequestBodyAsync(context);
        }

        private bool CanRead(Type modelType, string contentType)
        {
            var formatter = new FhirJsonInputFormatter(new FhirJsonParser(), ArrayPool<char>.Shared);
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

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class ValidateContentTypeFilterAttributeTests
    {
        private readonly IConformanceProvider _conformanceProvider;
        private readonly CapabilityStatement _statement;

        public ValidateContentTypeFilterAttributeTests()
        {
            _statement = new CapabilityStatement
            {
                Format = new List<string> { "json" },
            };

            _conformanceProvider = Substitute.For<IConformanceProvider>();
            _conformanceProvider.GetCapabilityStatementOnStartup().Returns(_statement.ToTypedElement().ToResourceElement());
        }

        [Theory]
        [InlineData("application/fhir+json")]
        [InlineData("application/json")]
        [InlineData("json")]
        public async Task GivenARequestWithAValidFormatQueryStringAndNoAcceptHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string requestFormat)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={requestFormat}");

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        [Fact]
        public async Task GivenARequestWithAValidFormatQueryStringAndAnEmptyAcceptHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown()
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format=json");
            context.HttpContext.Request.Headers["Accept"] = string.Empty;

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        [Theory]
        [InlineData("application/fhir+json")]
        [InlineData("application/json")]
        [InlineData("json")]
        public async Task GivenARequestWithAValidFormatQueryStringAndAValidAcceptHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string requestFormat)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={requestFormat}");
            context.HttpContext.Request.Headers["Accept"] = "application/json";

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("application/blah")]
        [InlineData("text/xml")]
        [InlineData("ttl")]
        [InlineData("xml")]
        [InlineData("blah")]
        public async Task GivenARequestWithAnInvalidFormatQueryString_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown(string requestFormat)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={requestFormat}");

            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData("json", "application/fhir+xml")]
        [InlineData("application/fhir+json", "application/fhir+xml")]
        public async Task GivenARequestWithAValidFormatQueryStringAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenAnUnsupportedMediaTypeExceptionShouldBeThrown(string requestFormat, string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={requestFormat}");
            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;
            context.HttpContext.Request.Headers["Accept"] = contentTypeHeader;

            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData("application/fhir+xml")]
        [InlineData("application/xml")]
        [InlineData("application/blah")]
        [InlineData("text/xml")]
        [InlineData("ttl")]
        [InlineData("xml")]
        [InlineData("blah")]
        public async Task GivenARequestWithAnInvalidFormatQueryStringAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown(string requestFormat)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={requestFormat}");

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = "application/fhir+xml";

            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/fhir+json")]
        [InlineData("application/json+fhir")]
        [InlineData("*/*")]
        [InlineData("application/xml,application/xhtml+xml,text/html;q=0.9, text/plain;q=0.8,image/png,*/*;q=0.5")]
        public async Task GivenARequestWithAValidAcceptHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string acceptHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        // Invalid Accept headers are ignored, unless they are formatted "application/<invalid_format>".
        [Theory]
        [InlineData("")]
        [InlineData("xml")]
        [InlineData("json")]
        [InlineData("blah")]
        public async Task GivenARequestWithAnInvalidAcceptHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string acceptHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        // Invalid Accept headers that are formatted "application/<invalid_format>" should throw an exception.
        [Theory]
        [InlineData("application/blah")]
        [InlineData("application/xml")]
        [InlineData("application/fhir+xml")]
        public async Task GivenARequestWithAnInvalidApplicationAcceptHeader_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown(string acceptHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        // Invalid Accept headers are ignored, unless they are formatted "application/<invalid_format>".
        [Theory]
        [InlineData("", "application/blah")]
        [InlineData("", "application/fhir+xml")]
        [InlineData("", "")]
        [InlineData("xml", "application/blah")]
        [InlineData("xml", "application/fhir+xml")]
        [InlineData("xml", "")]
        [InlineData("blah", "application/blah")]
        [InlineData("blah", "application/fhir+xml")]
        [InlineData("blah", "")]
        public async Task GivenARequestWithAnInvalidAcceptHeaderAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenAnUnsupportedMediaTypeExceptionShouldBeThrown(string acceptHeader, string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;

            // The fact that the Accept header is invalid is ignored, but an exception related to the invalid Content Type header is still thrown.
            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        // Invalid Accept headers that are formatted "application/<invalid_format>" should throw an exception.
        [Theory]
        [InlineData("application/blah", "application/blah")]
        [InlineData("application/blah", "application/fhir+xml")]
        [InlineData("application/blah", "")]
        [InlineData("application/fhir+xml", "application/blah")]
        [InlineData("application/fhir+xml", "application/fhir+xml")]
        [InlineData("application/fhir+xml", "")]
        public async Task GivenARequestWithAnInvalidApplicationAcceptHeaderAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown(string acceptHeader, string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;

            // The Accept header is invalid and has the format "application/<invalid_format>", so an exception for that is thrown.
            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData("application/blah")]
        [InlineData("application/fhir+xml")]
        [InlineData("")]
        public async Task GivenARequestWithNoAcceptHeaderAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenAnUnsupportedMediaTypeExceptionShouldBeThrown(string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;

            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/fhir+json")]
        [InlineData("application/json+fhir")]
        public async Task GivenARequestWithAValidAcceptHeaderAndAValidContentTypeHeader_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;
            context.HttpContext.Request.Headers["Accept"] = contentTypeHeader;

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
        }

        [Theory]
        [InlineData("application/blah")]
        [InlineData("application/xml")]
        [InlineData("application/fhir+xml")]
        [InlineData("")]
        public async Task GivenARequestWithAValidAcceptHeaderAndAnInvalidContentTypeHeader_WhenValidatingTheContentType_ThenAnUnsupportedMediaTypeExceptionShouldBeThrown(string contentTypeHeader)
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = "application/json";

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();
            context.HttpContext.Request.Headers["Content-Type"] = contentTypeHeader;

            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Fact]
        public async Task GivenARequestWithNoContentTypeHeader_WhenValidatingTheContentType_ThenAnUnsupportedMediaTypeExceptionShouldBeThrown()
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Method = HttpMethod.Post.ToString();

            await Assert.ThrowsAsync<UnsupportedMediaTypeException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData(new[] { "json", "application/fhir+json" }, "json")]
        [InlineData(new[] { "xml", "application/fhir+xml" }, "xml")]
        public async Task GivenACapabilityStatementWithMultipleFormats_WhenValidatingTheContentType_ThenNoExceptionShouldBeThrown(string[] formats, string formatQuerystring)
        {
            var filter = CreateFilter();

            var tempFormat = _statement.Format;
            _statement.Format = formats;

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.QueryString = new QueryString($"?_format={formatQuerystring}");
            context.HttpContext.Request.Headers["Accept"] = formats[1];

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);

            _statement.Format = tempFormat;
        }

        [Fact]
        public async Task GivenARequestWithAValidAcceptHeaderAndFormatOverride_WhenSettingTheContentType_ThenClientAcceptHeadersShouldBeConsidered()
        {
            string applicationXml = "application/xml";

            _statement.Format = new[] { "xml" };

            var xmlOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            xmlOutput.SupportedEncodings.Add(Encoding.UTF8);
            xmlOutput.SupportedMediaTypes.Add("application/fhir+xml");
            xmlOutput.SupportedMediaTypes.Add(applicationXml);
            xmlOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var filter = CreateFilter(new[] { xmlOutput });
            var acceptHeader = "application/xml,application/xhtml+xml,text/html;q=0.9, text/plain;q=0.8,image/png,*/*;q=0.5";

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;
            context.HttpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                { KnownQueryParameterNames.Format, "xml" },
            });

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);

            Assert.Equal(applicationXml, context.HttpContext.Response.ContentType);
        }

        [Theory]
        [InlineData("application/fhir+xml;charset=UTF-8", "application/fhir+xml; charset=utf-8")]
        [InlineData("application/fhir+xml;charset=utf-16", "application/fhir+xml; charset=utf-16")]
        [InlineData("application/fhir+xml;charset=UTF-8, */*;q=0.1", "application/fhir+xml; charset=utf-8")]
        [InlineData("application/fhir+xml;charset=iso-8859-1, application/fhir+json;q=0.9", "application/fhir+json")]
        [InlineData("application/fhir+xml;charset=utf-8;q=0.1, application/fhir+json;q=1.0", null)]
        [InlineData("application/fhir+xml;q=0.5, application/fhir+json;charset=utf-8;q=0.9", "application/fhir+json; charset=utf-8")]
        [InlineData("application/fhir+xml;Q=0.5, application/fhir+json;charset=utf-8;Q=0.9", "application/fhir+json; charset=utf-8")]
        public async Task GivenARequestWithAcceptHeaderParametersAndQualityFactors_WhenValidatingTheContentType_ThenHighestQualityPrecedenceAndResponseContentTypeShouldBeRespected(
            string acceptHeader,
            string expectedResponseContentType)
        {
            _statement.Format = new[] { "json", "xml" };

            var xmlOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            xmlOutput.SupportedEncodings.Add(Encoding.UTF8);
            xmlOutput.SupportedEncodings.Add(Encoding.Unicode);
            xmlOutput.SupportedMediaTypes.Add("application/fhir+xml");
            xmlOutput.SupportedMediaTypes.Add("application/xml");
            xmlOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var jsonOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            jsonOutput.SupportedEncodings.Add(Encoding.UTF8);
            jsonOutput.SupportedEncodings.Add(Encoding.Unicode);
            jsonOutput.SupportedMediaTypes.Add("application/fhir+json");
            jsonOutput.SupportedMediaTypes.Add("application/json");
            jsonOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var filter = CreateFilter(new[] { xmlOutput, jsonOutput });

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = acceptHeader;

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);

            Assert.Equal(expectedResponseContentType, context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task GivenARequestWithLegacyFhirJsonAliasAndUtf8Charset_WhenValidatingTheContentType_ThenCanonicalFormatterMediaTypeShouldBeUsed()
        {
            _statement.Format = new[] { "json" };

            var jsonOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            jsonOutput.SupportedEncodings.Add(Encoding.UTF8);
            jsonOutput.SupportedEncodings.Add(Encoding.Unicode);
            jsonOutput.SupportedMediaTypes.Add("application/fhir+json");
            jsonOutput.SupportedMediaTypes.Add("application/json");
            jsonOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var filter = CreateFilter(new[] { jsonOutput });

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = "application/json+fhir;charset=utf-8";

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);

            Assert.Equal("application/fhir+json; charset=utf-8", context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task GivenARequestWithUnsupportedAcceptCharsetAndNoFallback_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown()
        {
            _statement.Format = new[] { "json", "xml" };

            var xmlOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            xmlOutput.SupportedEncodings.Add(Encoding.UTF8);
            xmlOutput.SupportedEncodings.Add(Encoding.Unicode);
            xmlOutput.SupportedMediaTypes.Add("application/fhir+xml");
            xmlOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var filter = CreateFilter(new[] { xmlOutput });

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = "application/fhir+xml;charset=iso-8859-1";

            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData(true, null)]
        [InlineData(false, "application/fhir+json; charset=utf-8")]
        public async Task GivenARequestWithHigherPriorityHtmlAcceptHeader_WhenValidatingTheContentType_ThenRegisteredNonFhirFormattersShouldNotBeOverridden(
            bool registerHtmlFormatter,
            string expectedResponseContentType)
        {
            _statement.Format = new[] { "json", "xml" };

            var jsonOutput = Substitute.ForPartsOf<TextOutputFormatter>();
            jsonOutput.SupportedEncodings.Add(Encoding.UTF8);
            jsonOutput.SupportedMediaTypes.Add("application/fhir+json");
            jsonOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

            var formatters = new List<TextOutputFormatter> { jsonOutput };
            if (registerHtmlFormatter)
            {
                var htmlOutput = Substitute.ForPartsOf<TextOutputFormatter>();
                htmlOutput.SupportedEncodings.Add(Encoding.UTF8);
                htmlOutput.SupportedMediaTypes.Add("text/html");
                htmlOutput.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);
                formatters.Insert(0, htmlOutput);
            }

            var filter = CreateFilter(formatters);
            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = "text/html, application/fhir+json;charset=utf-8;q=0.9, */*;q=0.1";

            await filter.OnActionExecutionAsync(context, actionExecutedDelegate);

            Assert.Equal(expectedResponseContentType, context.HttpContext.Response.ContentType);
        }

        [Fact]
        public async Task GivenARequestWithZeroQualityAcceptHeader_WhenValidatingTheContentType_ThenANotAcceptableExceptionShouldBeThrown()
        {
            var filter = CreateFilter();

            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Headers["Accept"] = "application/fhir+json;q=0";

            await Assert.ThrowsAsync<NotAcceptableException>(async () => await filter.OnActionExecutionAsync(context, actionExecutedDelegate));
        }

        [Theory]
        [InlineData(new[] { "json", "application/fhir+json" }, "json", true)]
        [InlineData(new[] { "xml", "application/fhir+xml" }, "xml", true)]
        [InlineData(new[] { "xml", "application/fhir+xml" }, "json", false)]
        public async Task GivenAPatchRequestWithNoContentTypeHeader_WhenValidatingTheContentType_ThenTheContentTypeShouldBeValidatedCorrectly(
            string[] formats,
            string contentType,
            bool supported)
        {
            _statement.PatchFormat = formats;

            var filter = CreateFilter();
            var context = CreateContext(Guid.NewGuid().ToString());
            var actionExecutedDelegate = CreateActionExecutedDelegate(context);

            context.HttpContext.Request.Method = HttpMethod.Patch.ToString();
            context.HttpContext.Request.Headers[HeaderNames.ContentType] = contentType;

            try
            {
                await filter.OnActionExecutionAsync(context, actionExecutedDelegate);
                Assert.True(supported);
            }
            catch (UnsupportedMediaTypeException)
            {
                Assert.False(supported);
            }
        }

        [Theory]
        [InlineData("/Patient/example", "application/fhir+xml;charset=UTF-8", "application/fhir+xml", "utf-8")]
        [InlineData("/raw/Patient/example", "application/fhir+xml;charset=UTF-8", "application/fhir+xml", "utf-8")]
        [InlineData("/Patient/example", "application/fhir+json;charset=utf-16", "application/fhir+json", "utf-16")]
        [InlineData("/raw/Patient/example", "application/fhir+json;charset=utf-16", "application/fhir+json", "utf-16")]
        [InlineData("/Patient/example", "application/json+fhir;charset=utf-8", "application/fhir+json", "utf-8")]
        [InlineData("/Patient/example", "application/fhir+xml;charset=iso-8859-1, application/fhir+json;charset=utf-8;q=0.8", "application/fhir+json", "utf-8")]
        public async Task GivenAnAcceptHeaderWithCharset_WhenExecutedThroughMvc_ThenExpectedFormatterAndEncodingSerializeTheResponse(
            string requestPath,
            string acceptHeader,
            string expectedMediaType,
            string expectedCharSet)
        {
            using IHost host = CreateFormatterHost("json", "xml");
            using HttpClient client = host.GetTestClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            Assert.True(request.Headers.TryAddWithoutValidation(HeaderNames.Accept, acceptHeader));

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Content.Headers.ContentType);
            Assert.Equal(expectedMediaType, response.Content.Headers.ContentType.MediaType);
            Assert.Equal(expectedCharSet, response.Content.Headers.ContentType.CharSet);

            byte[] rawBytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(rawBytes);

            Encoding encoding = string.Equals(expectedCharSet, "utf-16", StringComparison.OrdinalIgnoreCase)
                ? Encoding.Unicode
                : Encoding.UTF8;
            string payload = encoding.GetString(rawBytes).TrimStart('\uFEFF');

            Patient patient = expectedMediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                ? new FhirXmlParser().Parse<Patient>(payload)
                : new FhirJsonParser().Parse<Patient>(payload);

            Assert.Equal("example", patient.Id);
        }

        private IHost CreateFormatterHost(params string[] supportedFormats)
        {
            _statement.Format = supportedFormats;

            var jsonFormatter = new FhirJsonOutputFormatter(
                new FhirJsonSerializer(),
                Deserializers.ResourceDeserializer,
                ArrayPool<char>.Shared,
                new BundleSerializer(new FhirJsonSerializer()),
                ModelInfoProvider.Instance);
            var xmlFormatter = new FhirXmlOutputFormatter(
                new FhirXmlSerializer(),
                Deserializers.ResourceDeserializer,
                ModelInfoProvider.Instance);

            var validator = new FormatParametersValidator(
                _conformanceProvider,
                new TextOutputFormatter[] { jsonFormatter, xmlFormatter });

            return new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IFormatParametersValidator>(validator);
                    services.AddSingleton<ValidateFormatParametersAttribute>();
                    services.AddControllers(options =>
                    {
                        options.EnableEndpointRouting = true;
                        options.RespectBrowserAcceptHeader = true;
                        options.OutputFormatters.Clear();
                        options.OutputFormatters.Add(jsonFormatter);
                        options.OutputFormatters.Add(xmlFormatter);
                    }).ConfigureApplicationPartManager(manager =>
                    {
                        manager.ApplicationParts.Clear();
                        manager.FeatureProviders.Add(new FormatTestControllerFeatureProvider());
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).Start();
        }

        private ValidateFormatParametersAttribute CreateFilter(IReadOnlyCollection<TextOutputFormatter> formatters = null)
        {
            if (formatters == null)
            {
                var formatter = Substitute.For<TextOutputFormatter>();
                formatter.SupportedMediaTypes.Add("application/fhir+xml");
                formatter.SupportedMediaTypes.Add("application/fhir+json");
                formatter.CanWriteResult(Arg.Any<OutputFormatterCanWriteContext>()).Returns(true);

                formatters = new[] { formatter };
            }

            var service = new FormatParametersValidator(_conformanceProvider, formatters);

            return new ValidateFormatParametersAttribute(service);
        }

        private static ActionExecutingContext CreateContext(string id)
        {
            return new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData { Values = { ["type"] = "Observation", ["id"] = id } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());
        }

        private static ActionExecutionDelegate CreateActionExecutedDelegate(ActionExecutingContext context)
        {
            var actionExecutedContext = new ActionExecutedContext(context, context.Filters, context.Controller)
            {
                Result = context.Result,
            };
            return () => Task.FromResult(actionExecutedContext);
        }

        [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
        public sealed class FormatTestController : ControllerBase
        {
            [HttpGet("/Patient/{id}")]
            public IActionResult GetPatient(string id)
            {
                return Ok(new Patient { Id = id });
            }

            [HttpGet("/raw/Patient/{id}")]
            public IActionResult GetRawPatient(string id)
            {
                var patient = new Patient { Id = id };
                var wrapper = new ResourceWrapper(
                    patient.ToResourceElement(),
                    new RawResource(patient.ToJson(), FhirResourceFormat.Json, false),
                    null,
                    false,
                    null,
                    null,
                    null);
                return Ok(new RawResourceElement(wrapper));
            }
        }

        private sealed class FormatTestControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
        {
            public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            {
                feature.Controllers.Add(typeof(FormatTestController).GetTypeInfo());
            }
        }
    }
}

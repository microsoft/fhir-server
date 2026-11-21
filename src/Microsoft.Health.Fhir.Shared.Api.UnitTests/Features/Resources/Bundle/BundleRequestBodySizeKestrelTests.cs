// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    /// <summary>
    /// Runs real Kestrel so request body limits are enforced the same way as in production.
    /// Kestrel's default limit is lowered to keep the request bodies small.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public sealed class BundleRequestBodySizeKestrelTests
    {
        private const long KestrelDefaultLimit = 2_000;
        private const long BundleLimit = 6_000;
        private const int AcceptedBodySize = 4_000;
        private const int RejectedBodySize = 9_000;

        private readonly IFailureMetricHandler _failureMetricHandler = Substitute.For<IFailureMetricHandler>();

        public enum BodyFormat
        {
            Json,
            Xml,
        }

        [Theory]
        [InlineData(BodyFormat.Json, true, false)]
        [InlineData(BodyFormat.Json, false, false)]
        [InlineData(BodyFormat.Xml, true, false)]
        [InlineData(BodyFormat.Xml, false, false)]
        [InlineData(BodyFormat.Json, false, true)]
        [InlineData(BodyFormat.Xml, false, true)]
        public async Task GivenRestrictionsEnabled_WhenABundleIsBetweenTheKestrelDefaultAndTheBundleLimit_ThenItIsAccepted(BodyFormat format, bool sendContentLength, bool withPaasByteCounting)
        {
            await using RunningServer server = await StartServerAsync(enabled: true, withPaasByteCounting);

            HttpResponseMessage response = await server.PostBundleAsync(CreateBundleBody(format, AcceptedBodySize), format, sendContentLength);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _failureMetricHandler.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
        }

        [Theory]
        [InlineData(BodyFormat.Json, true, false)]
        [InlineData(BodyFormat.Json, false, false)]
        [InlineData(BodyFormat.Xml, true, false)]
        [InlineData(BodyFormat.Xml, false, false)]
        [InlineData(BodyFormat.Json, false, true)]
        [InlineData(BodyFormat.Xml, false, true)]
        public async Task GivenRestrictionsEnabled_WhenABundleExceedsTheBundleLimit_ThenRequestEntityTooLargeIsReturned(BodyFormat format, bool sendContentLength, bool withPaasByteCounting)
        {
            await using RunningServer server = await StartServerAsync(enabled: true, withPaasByteCounting);

            HttpResponseMessage response = await server.PostBundleAsync(CreateBundleBody(format, RejectedBodySize), format, sendContentLength);

            string body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.RequestEntityTooLarge, $"{(int)response.StatusCode}: {body}");
            Assert.Contains("OperationOutcome", body, StringComparison.Ordinal);
            Assert.Contains(BundleLimit.ToString(), body, StringComparison.Ordinal);
            _failureMetricHandler.Received(1).EmitException(Arg.Is<IExceptionMetricNotification>(n => n.ExceptionType == "BundleSizeLimitExceeded"));
        }

        [Theory]
        [InlineData(BodyFormat.Json, true)]
        [InlineData(BodyFormat.Json, false)]
        public async Task GivenRestrictionsDisabled_WhenABundleExceedsTheKestrelDefault_ThenTheExistingBadRequestIsReturned(BodyFormat format, bool sendContentLength)
        {
            await using RunningServer server = await StartServerAsync(enabled: false, withPaasByteCounting: false);

            HttpResponseMessage response = await server.PostBundleAsync(CreateBundleBody(format, AcceptedBodySize), format, sendContentLength);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            _failureMetricHandler.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
        }

        private async Task<RunningServer> StartServerAsync(bool enabled, bool withPaasByteCounting)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Limits.MaxRequestBodySize = KestrelDefaultLimit;
            });

            builder.Services.AddSingleton(Options.Create(new BundleConfiguration { EnableSizeRestrictions = enabled, MaxBundleSizeBytes = BundleLimit }));
            builder.Services.AddSingleton(Options.Create(new FhirServerConfiguration()));
            builder.Services.AddSingleton(_failureMetricHandler);
            builder.Services.AddSingleton(Substitute.For<IFormatParametersValidator>());
            builder.Services.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(new FhirRequestContextAccessor());

            builder.Services
                .AddControllers(options =>
                {
                    options.InputFormatters.Insert(0, new FhirJsonInputFormatter(new FhirJsonParser(), ArrayPool<char>.Shared));
                    options.InputFormatters.Insert(0, new FhirXmlInputFormatter(new FhirXmlParser()));
                    options.OutputFormatters.Insert(0, new FhirResourceOutputFormatter());
                })
                .ConfigureApplicationPartManager(manager =>
                {
                    foreach (ControllerFeatureProvider provider in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToList())
                    {
                        manager.FeatureProviders.Remove(provider);
                    }

                    manager.FeatureProviders.Add(new SingleControllerFeatureProvider());
                });

            WebApplication app = builder.Build();

            // Stands in for FhirRequestContextMiddleware, which runs before the bundle middleware in the real pipeline.
            app.Use(async (ctx, next) =>
            {
                ctx.RequestServices.GetRequiredService<RequestContextAccessor<IFhirRequestContext>>().RequestContext = new DefaultFhirRequestContext();
                await next();
            });
            app.UseMiddleware<BundleRequestBodySizeMiddleware>();

            if (withPaasByteCounting)
            {
                app.Use(CountRequestSizeLikeFhirPaasAsync);
            }

            app.MapControllers();
            await app.StartAsync();

            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
            return new RunningServer(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        // Mirrors fhir-paas ByteCountingStreamMiddleware: buffers bodies without Content-Length before MVC reads them and swallows failures.
        private static async Task CountRequestSizeLikeFhirPaasAsync(HttpContext context, Func<Task> next)
        {
            if (context.Request.ContentLength is null)
            {
                try
                {
                    context.Request.EnableBuffering();
                    using var stream = new MemoryStream();
                    await context.Request.Body.CopyToAsync(stream);
                }
                catch (Exception)
                {
                    // fhir-paas logs and continues.
                }
                finally
                {
                    if (context.Request.Body.CanSeek)
                    {
                        context.Request.Body.Seek(0, SeekOrigin.Begin);
                    }
                }
            }
            else
            {
                context.Request.EnableBuffering();
            }

            await next();
        }

        private static string CreateBundleBody(BodyFormat format, int targetSize)
        {
            var patient = new Patient { Id = "p1" };
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Hl7.Fhir.Model.Bundle.BundleType.Batch,
                Entry = new List<Hl7.Fhir.Model.Bundle.EntryComponent>
                {
                    new Hl7.Fhir.Model.Bundle.EntryComponent
                    {
                        Request = new Hl7.Fhir.Model.Bundle.RequestComponent { Method = Hl7.Fhir.Model.Bundle.HTTPVerb.PUT, Url = "Patient/p1" },
                        Resource = patient,
                    },
                },
            };

            string Serialize() => format == BodyFormat.Json ? new FhirJsonSerializer().SerializeToString(bundle) : new FhirXmlSerializer().SerializeToString(bundle);

            int padding = Math.Max(0, targetSize - Encoding.UTF8.GetByteCount(Serialize()) - 100);
            patient.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\">{new string('a', padding)}</div>",
            };

            return Serialize();
        }

        private sealed class RunningServer : IAsyncDisposable
        {
            private readonly WebApplication _app;
            private readonly HttpClient _client;

            public RunningServer(WebApplication app, HttpClient client)
            {
                _app = app;
                _client = client;
            }

            public async Task<HttpResponseMessage> PostBundleAsync(string body, BodyFormat format, bool sendContentLength)
            {
                string mediaType = format == BodyFormat.Json ? "application/fhir+json" : "application/fhir+xml";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                HttpContent content = sendContentLength ? new ByteArrayContent(bytes) : new ChunkedContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

                return await _client.PostAsync("/", content);
            }

            public async ValueTask DisposeAsync()
            {
                _client.Dispose();
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        private sealed class ChunkedContent : HttpContent
        {
            private readonly byte[] _bytes;

            public ChunkedContent(byte[] bytes) => _bytes = bytes;

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(_bytes, 0, _bytes.Length);

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

        private sealed class FhirResourceOutputFormatter : TextOutputFormatter
        {
            public FhirResourceOutputFormatter()
            {
                SupportedEncodings.Add(Encoding.UTF8);
                SupportedMediaTypes.Add("application/fhir+json");
                SupportedMediaTypes.Add("application/json");
            }

            protected override bool CanWriteType(Type type) => typeof(Resource).IsAssignableFrom(type);

            public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding) =>
                context.HttpContext.Response.WriteAsync(new FhirJsonSerializer().SerializeToString((Resource)context.Object), selectedEncoding);
        }

        private sealed class SingleControllerFeatureProvider : ControllerFeatureProvider
        {
            protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(BundleUploadController);
        }

        /// <summary>
        /// Minimal controller that model-binds a bundle the same way the FHIR controller does.
        /// </summary>
        public sealed class BundleUploadController : ControllerBase
        {
            [HttpPost("")]
            public IActionResult Post([FromBody] Resource bundle) => ModelState.IsValid && bundle != null ? Ok() : BadRequest();
        }
    }
}

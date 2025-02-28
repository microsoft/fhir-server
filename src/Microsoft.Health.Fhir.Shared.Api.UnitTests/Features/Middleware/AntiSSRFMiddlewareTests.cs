// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Middleware;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.Features.Health;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.DataSourceValidation)]
public class AntiSSRFMiddlewareTests
{
    private readonly NetworkConfiguration _networkOptions;
    private readonly ForwardedHeadersOptions _forwardOptions;
    private readonly AntiSSRFMiddleware _middleware;
    private readonly HttpContext _context;

    public AntiSSRFMiddlewareTests()
    {
        IWebHostEnvironment environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("UnitTest");
        _forwardOptions = new ForwardedHeadersOptions();
        _networkOptions = new NetworkConfiguration();
        _middleware = new AntiSSRFMiddleware(InvokeAsync, Options.Create(_networkOptions), Options.Create(_forwardOptions), NullLogger<AntiSSRFMiddleware>.Instance);
        _context = new DefaultHttpContext();
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("192.0.0.66")]
    [InlineData("unit-test.fhir.azurehealthcareapis.com")]
    public async Task GivenUnchangedHost_WhenInvokingMiddleware_ThenInvokeNextHandler(string hostOrAddress)
    {
        _networkOptions.ServiceUrl = new Uri($"https://{hostOrAddress}");
        _context.Request.Host = new HostString(_networkOptions.ServiceUrl.Host);

        await _middleware.InvokeAsync(_context);
        Assert.Equal((int)HttpStatusCode.Accepted, _context.Response.StatusCode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("192.0.0.66")]
    [InlineData("unit-test.fhir.azurehealthcareapis.com")]
    public async Task GivenSameForwardedHost_WhenInvokingMiddleware_ThenInvokeNextHandler(string hostOrAddress)
    {
        _networkOptions.ServiceUrl = new Uri($"https://{hostOrAddress}");
        _context.Request.Host = new HostString(_networkOptions.ServiceUrl.Host);
        _context.Request.Headers.Append(_forwardOptions.ForwardedHostHeaderName, hostOrAddress);
        _context.Request.Headers.Append(_forwardOptions.OriginalHostHeaderName, hostOrAddress);

        await _middleware.InvokeAsync(_context);
        Assert.Equal((int)HttpStatusCode.Accepted, _context.Response.StatusCode);
    }

    [Theory]
    [InlineData("168.63.129.16")] // WireServer
    [InlineData("169.254.169.254")] // IMDS
    [InlineData("0.0.0.0")]
    [InlineData("127.0.200.8")] // localhost
    [InlineData("0177.0.23.19")] // localhost in octal form
    [InlineData("2130706433")] // localhost as a 32 bit integer
    [InlineData("0x7f.00331.0246.174")]
    [InlineData("172.16.0.99")]
    [InlineData("192.0.0.66")]
    [InlineData("192.0.2.77")]
    [InlineData("192.88.99.36")]
    [InlineData("192.168.0.101")]
    [InlineData("25.0.1.2")]
    [InlineData("100.64.0.123")]
    [InlineData("198.51.100.52")]
    [InlineData("203.0.113.189")]
    [InlineData("224.0.1.2")]
    [InlineData("240.0.1.2")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fc00::")]
    [InlineData("localhost")]
    [InlineData("imds.michaelhendrickx.com")]
    public async Task GivenInvalidForwardedHost_WhenInvokingMiddleware_ThenThrowServerSideRequestForgeryException(string host)
    {
        const string OriginalHost = "microsoft.com";

        _networkOptions.ServiceUrl = new Uri("https://" + OriginalHost);
        _context.Request.Scheme = "https";
        _context.Request.Host = new HostString(host);
        _context.Request.Headers.Append(_forwardOptions.ForwardedHostHeaderName, host);
        _context.Request.Headers.Append(_forwardOptions.OriginalHostHeaderName, OriginalHost);

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => _middleware.InvokeAsync(_context));
    }

    private static Task InvokeAsync(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Accepted;
        return Task.CompletedTask;
    }
}

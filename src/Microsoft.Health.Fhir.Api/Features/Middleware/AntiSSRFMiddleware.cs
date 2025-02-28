// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Internal.AntiSSRF;
using Microsoft.Internal.AntiSSRF.ExceptionHandling;

namespace Microsoft.Health.Fhir.Api.Features.Middleware;

public sealed class AntiSSRFMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AntiSSRFPolicy _antiSSRFPolicy;
    private readonly NetworkConfiguration _networkOptions;
    private readonly ForwardedHeadersOptions _forwardOptions;
    private readonly ILogger<AntiSSRFMiddleware> _logger;

    public AntiSSRFMiddleware(
        RequestDelegate next,
        IOptions<NetworkConfiguration> networkOptions,
        IOptions<ForwardedHeadersOptions> forwardOptions,
        ILogger<AntiSSRFMiddleware> logger)
    {
        _next = EnsureArg.IsNotNull(next, nameof(next));
        _forwardOptions = EnsureArg.IsNotNull(forwardOptions?.Value, nameof(forwardOptions));
        _networkOptions = EnsureArg.IsNotNull(networkOptions?.Value, nameof(networkOptions));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));

        _antiSSRFPolicy = new AntiSSRFPolicy();
        _antiSSRFPolicy.SetDefaults();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        HttpRequest request = EnsureArg.IsNotNull(context, nameof(context)).Request;

        if (request.Headers.TryGetValue(_forwardOptions.ForwardedForHeaderName, out StringValues headerValue))
        {
            _logger.LogWarning(
                "Request contains X-Forwarded-For header with value '{XForwardedFor}'.",
                headerValue.ToString());
        }

        if (IncludesCustomForwardedHost(request) && await IsNonroutableHostAsync(request, context.RequestAborted))
        {
            throw new ServerSideRequestForgeryException(Api.Resources.NonroutableHost);
        }

        await _next(context);
    }

    private bool IncludesCustomForwardedHost(HttpRequest request)
        => request.Headers.ContainsKey(_forwardOptions.OriginalHostHeaderName) && !request.Host.Host.Equals(_networkOptions.ServiceUrl.Host, StringComparison.Ordinal);

    private async ValueTask<bool> IsNonroutableHostAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        Uri baseUri = new UriBuilder { Scheme = request.Scheme, Host = request.Host.Host, Port = request.Host.Port ?? -1 }.Uri;

        try
        {
            // URIValidate only checks the host for the URL, so we do not need to re-validate
            // it for each derived URL we return in the response
            if (URIValidate.IsNonroutableNetworkAddress(baseUri, _antiSSRFPolicy))
            {
                await LogSSRFExceptionAsync(request, cancellationToken: cancellationToken);
                return true;
            }
        }
        catch (AntiSSRFException ex)
        {
            // In some scenarios, the method throws an exception instead... unfortunately
            await LogSSRFExceptionAsync(request, ex, cancellationToken);
            return true;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Used only for diagnostics.")]
    private async ValueTask LogSSRFExceptionAsync(HttpRequest request, AntiSSRFException exception = null, CancellationToken cancellationToken = default)
    {
        IPAddress address = null;
        try
        {
            IPAddress[] addressList = await Dns.GetHostAddressesAsync(request.Host.Host, cancellationToken);
            if (addressList.Length > 0)
            {
                address = addressList[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to resolve DNS for host '{Host}'.", request.Host.Host);
        }

        _logger.LogError(
            exception,
            "Request host '{Host}' with IP address '{Address}' is non-routable. Included X-Forwarded-Host header: {HasHostHeader}. Is private link: {IsPrivateLink}.",
            request.Host.Host,
            address,
            request.Headers.ContainsKey(_forwardOptions.ForwardedHostHeaderName),
            _networkOptions.IsPrivate);
    }
}

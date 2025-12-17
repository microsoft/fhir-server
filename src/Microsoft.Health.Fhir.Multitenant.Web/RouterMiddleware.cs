// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using Microsoft.Health.Fhir.Multitenant.Core;

namespace Microsoft.Health.Fhir.Multitenant.Web;

/// <summary>
/// Middleware that routes incoming requests to the appropriate tenant FHIR server instance.
/// </summary>
public class RouterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantManager _tenantManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RouterMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouterMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="tenantManager">The tenant manager.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public RouterMiddleware(
        RequestDelegate next,
        ITenantManager tenantManager,
        IHttpClientFactory httpClientFactory,
        ILogger<RouterMiddleware> logger)
    {
        _next = next;
        _tenantManager = tenantManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = ExtractTenantId(context.Request);

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Tenant ID not found in request");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Tenant ID is required. Provide X-Tenant-Id header or use path prefix /tenant-id/...");
            return;
        }

        var port = _tenantManager.GetTenantPort(tenantId);
        if (port == null)
        {
            _logger.LogWarning("Tenant {TenantId} not found", tenantId);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync($"Tenant '{tenantId}' not found");
            return;
        }

        await ProxyRequestAsync(context, port.Value, tenantId);
    }

    private async Task ProxyRequestAsync(HttpContext context, int port, string tenantId)
    {
        var targetPath = GetTargetPath(context.Request, tenantId);
        var targetUrl = $"http://localhost:{port}{targetPath}{context.Request.QueryString}";

        _logger.LogDebug(
            "Routing request for tenant {TenantId} to {TargetUrl}",
            tenantId,
            targetUrl);

        // Create a new HttpClient for each request using the factory
        using var httpClient = _httpClientFactory.CreateClient("RouterClient");

        using var proxyRequest = new HttpRequestMessage
        {
            Method = new HttpMethod(context.Request.Method),
            RequestUri = new Uri(targetUrl),
        };

        // Copy headers, excluding hop-by-hop headers
        foreach (var header in context.Request.Headers)
        {
            if (IsHopByHopHeader(header.Key))
            {
                continue;
            }

            proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Copy body if present - handle both Content-Length and Transfer-Encoding: chunked
        bool hasBody = context.Request.ContentLength > 0 ||
            context.Request.Headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
            transferEncoding.Contains("chunked");

        if (hasBody)
        {
            // Enable request body buffering for rewinding
            context.Request.EnableBuffering();

            // Read the entire body into a memory stream to avoid issues with non-rewindable streams
            var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream, context.RequestAborted);
            memoryStream.Position = 0;

            proxyRequest.Content = new StreamContent(memoryStream);

            if (!string.IsNullOrEmpty(context.Request.ContentType))
            {
                proxyRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }

            if (context.Request.ContentLength.HasValue)
            {
                proxyRequest.Content.Headers.ContentLength = context.Request.ContentLength;
            }
        }

        try
        {
            using var response = await httpClient.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            // Copy response headers
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (IsHopByHopHeader(header.Key))
                {
                    continue;
                }

                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error proxying request to tenant {TenantId}", tenantId);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync($"Error connecting to tenant '{tenantId}' service");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == context.RequestAborted)
        {
            _logger.LogDebug("Request cancelled by client for tenant {TenantId}", tenantId);
        }
    }

    private static string? ExtractTenantId(HttpRequest request)
    {
        // Option 1: X-Tenant-Id header (highest priority)
        if (request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
        {
            return headerValue.FirstOrDefault();
        }

        // Option 2: Path prefix (/tenant-id/Patient/123)
        var pathSegments = request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments?.Length > 0)
        {
            return pathSegments[0];
        }

        // Option 3: Subdomain (tenant1.fhir.example.com)
        var host = request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length > 2)
        {
            return parts[0];
        }

        return null;
    }

    private static string GetTargetPath(HttpRequest request, string tenantId)
    {
        // If tenant ID was in the path, remove it from the target path
        var path = request.Path.Value ?? "/";

        // Check if path starts with the tenant ID
        if (path.StartsWith($"/{tenantId}", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(tenantId.Length + 1);
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }
        }

        return path;
    }

    private static bool IsHopByHopHeader(string headerName)
    {
        return string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "TE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Trailer", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Upgrade", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase);
    }
}

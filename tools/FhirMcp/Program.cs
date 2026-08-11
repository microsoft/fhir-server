// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Mcp;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
FhirMcpOptions options = FhirMcpOptions.FromEnvironment();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Information;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FhirTools>();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(_ =>
{
    var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
    if (options.AllowInsecureLocalhost)
    {
        handler.ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.IsLoopback == true;
    }

    return new HttpClient(handler, disposeHandler: true);
});
builder.Services.AddSingleton<IFhirAccessTokenProvider, FhirAccessTokenProvider>();
builder.Services.AddSingleton<IFhirCaptureWriter, FhirCaptureWriter>();
builder.Services.AddSingleton<IFhirClient, FhirClient>();

await builder.Build().RunAsync();

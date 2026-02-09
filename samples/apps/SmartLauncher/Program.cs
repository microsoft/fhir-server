// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Internal.SmartLauncher.Models;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Serves the launcher configuration to the frontend.
app.MapGet("/config", (IConfiguration configuration) =>
{
    var config = new SmartLauncherConfig();
    configuration.Bind(config);
    return Results.Json(config);
});

// Proxies token requests for confidential clients, keeping secrets server-side.
app.MapPost("/token-proxy", async (HttpRequest request, IConfiguration configuration, IHttpClientFactory httpClientFactory) =>
{
    var config = new SmartLauncherConfig();
    configuration.Bind(config);

    var form = await request.ReadFormAsync();
    var tokenEndpoint = form["token_endpoint"].ToString();

    if (string.IsNullOrEmpty(tokenEndpoint))
    {
        return Results.BadRequest(new { error = "token_endpoint is required" });
    }

    var tokenRequestParams = new Dictionary<string, string>
    {
        ["grant_type"] = form["grant_type"].ToString(),
        ["code"] = form["code"].ToString(),
        ["redirect_uri"] = form["redirect_uri"].ToString(),
        ["code_verifier"] = form["code_verifier"].ToString(),
    };

    // For refresh token grants
    if (form.ContainsKey("refresh_token"))
    {
        tokenRequestParams["refresh_token"] = form["refresh_token"].ToString();
    }

    if (form.ContainsKey("scope"))
    {
        tokenRequestParams["scope"] = form["scope"].ToString();
    }

    var httpClient = httpClientFactory.CreateClient();
    var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
    {
        Content = new FormUrlEncodedContent(tokenRequestParams),
    };

    if (string.Equals(config.ClientType, "confidential-symmetric", StringComparison.OrdinalIgnoreCase))
    {
        var clientSecret = configuration["ClientSecret"] ?? string.Empty;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ClientId}:{clientSecret}"));
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
    else if (string.Equals(config.ClientType, "confidential-asymmetric", StringComparison.OrdinalIgnoreCase))
    {
        var assertion = GenerateClientAssertion(config.ClientId, tokenEndpoint, configuration);
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>(tokenRequestParams)
        {
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion,
        });
    }

    var response = await httpClient.SendAsync(tokenRequest);
    var content = await response.Content.ReadAsStringAsync();

    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
});

app.Run();

static string GenerateClientAssertion(string clientId, string tokenEndpoint, IConfiguration configuration)
{
    var certPath = configuration["CertificatePath"];
    var certPassword = configuration["CertificatePassword"];
    var certThumbprint = configuration["CertificateThumbprint"];

    X509Certificate2 cert;
    if (!string.IsNullOrEmpty(certPath))
    {
#if NET9_0_OR_GREATER
        cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, string.IsNullOrEmpty(certPassword) ? null : certPassword);
#else
        cert = string.IsNullOrEmpty(certPassword)
            ? new X509Certificate2(certPath)
            : new X509Certificate2(certPath, certPassword);
#endif
    }
    else if (!string.IsNullOrEmpty(certThumbprint))
    {
        using var store = new X509Store(StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certThumbprint, validOnly: false);
        cert = certs.Count > 0 ? certs[0] : throw new InvalidOperationException($"Certificate with thumbprint '{certThumbprint}' not found.");
    }
    else
    {
        throw new InvalidOperationException("CertificatePath or CertificateThumbprint must be configured for asymmetric authentication.");
    }

    var signingCredentials = new SigningCredentials(new X509SecurityKey(cert), SecurityAlgorithms.RsaSha384);
    var now = DateTime.UtcNow;

    var token = new JwtSecurityToken(
        issuer: clientId,
        audience: tokenEndpoint,
        claims: new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        },
        notBefore: now,
        expires: now.AddMinutes(5),
        signingCredentials: signingCredentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

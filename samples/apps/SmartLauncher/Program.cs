// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Internal.SmartLauncher.Models;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// GET /config — serves public configuration (no secrets)
app.MapGet("/config", (IConfiguration configuration) =>
{
    var config = new SmartLauncherConfig();
    configuration.Bind(config);
    return Results.Ok(config);
});

// POST /token-proxy — proxies token exchange for confidential clients
app.MapPost("/token-proxy", async (HttpRequest request, IConfiguration configuration, IHttpClientFactory httpClientFactory) =>
{
    var form = await request.ReadFormAsync();
    var tokenEndpoint = form["token_endpoint"].ToString();
    var grantType = form["grant_type"].ToString();
    var code = form["code"].ToString();
    var redirectUri = form["redirect_uri"].ToString();
    var codeVerifier = form["code_verifier"].ToString();
    var clientId = configuration["ClientId"] ?? string.Empty;
    var clientType = configuration["ClientType"] ?? "public";

    if (string.IsNullOrEmpty(tokenEndpoint))
    {
        return Results.BadRequest(new { error = "token_endpoint is required" });
    }

    var tokenRequestParams = new Dictionary<string, string>
    {
        ["grant_type"] = grantType,
        ["code"] = code,
        ["redirect_uri"] = redirectUri,
        ["client_id"] = clientId,
    };

    if (!string.IsNullOrEmpty(codeVerifier))
    {
        tokenRequestParams["code_verifier"] = codeVerifier;
    }

    using var httpClient = httpClientFactory.CreateClient();
    var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
    {
        Content = new FormUrlEncodedContent(tokenRequestParams),
    };

    if (clientType.Equals("confidential-symmetric", StringComparison.OrdinalIgnoreCase))
    {
        var clientSecret = configuration["ClientSecret"] ?? string.Empty;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }
    else if (clientType.Equals("confidential-asymmetric", StringComparison.OrdinalIgnoreCase))
    {
        var assertion = GenerateClientAssertion(clientId, tokenEndpoint, configuration);
        tokenRequestParams["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
        tokenRequestParams["client_assertion"] = assertion;
        tokenRequest.Content = new FormUrlEncodedContent(tokenRequestParams);
    }

    var response = await httpClient.SendAsync(tokenRequest);
    var content = await response.Content.ReadAsStringAsync();

    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
});

app.Run();

static string GenerateClientAssertion(string clientId, string tokenEndpoint, IConfiguration configuration)
{
    X509Certificate2 cert = LoadCertificate(configuration);

    var securityKey = new X509SecurityKey(cert);
    var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(
        issuer: clientId,
        audience: tokenEndpoint,
        claims: new[]
        {
            new System.Security.Claims.Claim("sub", clientId),
            new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString()),
        },
        notBefore: now,
        expires: now.AddMinutes(5),
        signingCredentials: signingCredentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

static X509Certificate2 LoadCertificate(IConfiguration configuration)
{
    var certPath = configuration["CertificatePath"];
    var certPassword = configuration["CertificatePassword"];
    var certThumbprint = configuration["CertificateThumbprint"];

    if (!string.IsNullOrEmpty(certPath))
    {
#pragma warning disable SYSLIB0057 // X509Certificate2 constructor is obsolete in .NET 9+
        return new X509Certificate2(certPath, certPassword);
#pragma warning restore SYSLIB0057
    }

    if (!string.IsNullOrEmpty(certThumbprint))
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, certThumbprint, validOnly: false);
        if (certs.Count == 0)
        {
            throw new InvalidOperationException($"Certificate with thumbprint '{certThumbprint}' not found in CurrentUser\\My store.");
        }

        return certs[0];
    }

    throw new InvalidOperationException("No certificate configured. Set either CertificatePath or CertificateThumbprint in appsettings.json.");
}

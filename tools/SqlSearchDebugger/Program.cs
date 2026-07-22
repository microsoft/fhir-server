// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using SqlSearchDebugger;
using SqlSearchDebugger.Mocks;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Initialize the FHIR model and parser
var modelInfoProvider = new VersionSpecificModelInfoProvider();
ModelInfoProvider.SetProvider(modelInfoProvider);

var fhirModel = new FakeSqlServerFhirModel();
var searchParamDefManager = ParserHelpers.InitializeSearchParameterDefinitionManager(modelInfoProvider);
var sqlSearchParamDefManager = new SqlSearchParameterDefinitionManager(searchParamDefManager, fhirModel);
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SearchParameterSqlParser>();
var parser = new SearchParameterSqlParser(sqlSearchParamDefManager, fhirModel, logger);

Console.WriteLine("SQL Search Debugger initialized with {0} resource types and {1} search parameters",
    fhirModel.ResourceTypeCount, fhirModel.SearchParamCount);
Console.WriteLine("Open http://localhost:5200 in your browser");

// Serve static files from wwwroot
app.UseStaticFiles();

// Serve index.html at root
app.MapGet("/", () => Results.File(
    Path.Combine(app.Environment.WebRootPath, "index.html"), "text/html"));

// API endpoint to parse a FHIR URL into SQL
app.MapPost("/api/parse", (ParseRequest request) =>
{
    try
    {
        var result = ParserHelpers.ParseFhirUrl(request.Url, request.ContinuationToken, parser, fhirModel);
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message, stackTrace = ex.StackTrace }, statusCode: 200);
    }
});

// API endpoint to list known resource types
app.MapGet("/api/resource-types", () => Results.Json(fhirModel.GetAllResourceTypes()));

// API endpoint to list search params for a resource type
app.MapGet("/api/search-params/{resourceType}", (string resourceType) =>
{
    try
    {
        var typeId = fhirModel.GetResourceTypeId(resourceType);
        var parameters = sqlSearchParamDefManager.GetByResourceType(typeId);
        return Results.Json(parameters.Select(p => new
        {
            code = p.SearchParameterInfo.Code,
            type = p.SearchParameterInfo.Type.ToString(),
            url = p.SearchParameterInfo.Url?.ToString(),
            id = p.Id,
            description = p.SearchParameterInfo.Description,
            targets = p.SearchParameterInfo.TargetResourceTypes?.Select(t => t.ToString()),
        }));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 400);
    }
});

app.Run("http://localhost:5200");

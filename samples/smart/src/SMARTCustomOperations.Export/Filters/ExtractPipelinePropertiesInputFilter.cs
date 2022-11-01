// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTCustomOperations.Export.Configuration;

namespace SMARTCustomOperations.Export.Filters
{
    public class ExtractPipelinePropertiesInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly string _id;

        private static readonly Regex ExportCheckExpression = new(@"/?[A-Za-z0-9\-_]*/_operations/export/([A-Za-z0-9\\-]+)");
        private static readonly Regex GroupExportExpression = new(@"/?[A-Za-z0-9\-_]*/Group/([A-Za-z0-9\-]+)/\$export");
        private static readonly Regex GetExportFileExpression = new(@"/?[A-Za-z0-9\-_]*/_export/([A-Za-z0-9\-]+)/(.*)");

        public ExtractPipelinePropertiesInputFilter(ILogger<ExtractPipelinePropertiesInputFilter> logger)
        {
            _logger = logger;
            _id = Guid.NewGuid().ToString();
        }

        public event EventHandler<FilterErrorEventArgs>? OnFilterError;

        public string Name => nameof(ExtractPipelinePropertiesInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        public string Id => _id;

        public Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            _logger?.LogInformation("Entered {Name}", Name);

            SavePipelineTypeToProperties(context);
            ExtractOidClaimToProperties(context);

            return Task.FromResult(context);
        }

        public OperationContext SavePipelineTypeToProperties(OperationContext context)
        {
            var exportCheckMatch = ExportCheckExpression.Match(context.Request.RequestUri!.LocalPath.ToString());
            var groupExportMatch = GroupExportExpression.Match(context.Request.RequestUri!.LocalPath.ToString());
            var getExportFileMatch = GetExportFileExpression.Match(context.Request.RequestUri!.LocalPath.ToString());

            if (exportCheckMatch.Success)
            {
                context.Properties["PipelineType"] = ExportOperationType.ExportCheck.ToString();
                context.Properties["ExportOperationId"] = exportCheckMatch.Groups[1].Captures[0].ToString();
            }
            else if (groupExportMatch.Success)
            {
                context.Properties["PipelineType"] = ExportOperationType.GroupExport.ToString();
                context.Properties["GroupId"] = groupExportMatch.Groups[1].Captures[0].ToString();
            }
            else if (getExportFileMatch.Success)
            {
                context.Properties["PipelineType"] = ExportOperationType.GetExportFile.ToString();
                context.Properties["ContainerName"] = getExportFileMatch.Groups[1].Captures[0].ToString();
                context.Properties["RestOfPath"] = getExportFileMatch.Groups[2].Captures[0].ToString();
            }
            else
            {
                // default error event in WebPipeline.cs of toolkit
                OnFilterError?.Invoke(this, new FilterErrorEventArgs(name: Name, id: Id, fatal: true, error: new ArgumentException("This endpoint is not setup for this type of request."), code: HttpStatusCode.BadRequest));
            }

            return context;
        }

        public OperationContext ExtractOidClaimToProperties(OperationContext context)
        {
            var tokenDecoder = new JwtSecurityTokenHandler();

            // Ensure request has bearer token
            if (!tokenDecoder.CanReadToken(context.Request.Headers.Authorization?.Parameter))
            {
                // default error event in WebPipeline.cs of toolkit
                OnFilterError?.Invoke(this, new FilterErrorEventArgs(name: Name, id: Id, fatal: true, error: new SecurityException("Bearer token missing."), code: HttpStatusCode.Unauthorized));
                return context;
            }

            // Set and parse the token
            context.Properties.Add("token", context.Request.Headers!.Authorization!.Parameter);
            var jwtSecurityToken = (JwtSecurityToken)tokenDecoder.ReadToken(context.Request.Headers!.Authorization!.Parameter);

            // Ensure there is an OID claim
            if (!jwtSecurityToken.Payload.ContainsKey("oid"))
            {
                // default error event in WebPipeline.cs of toolkit
                OnFilterError?.Invoke(this, new FilterErrorEventArgs(name: Name, id: Id, fatal: true, error: new SecurityException("Bearer token must have OID claim."), code: HttpStatusCode.Unauthorized));
                return context;
            }

            // Set the object ID
            context.Properties.Add("oid", jwtSecurityToken.Payload["oid"].ToString());

            return context;
        }
    }
}

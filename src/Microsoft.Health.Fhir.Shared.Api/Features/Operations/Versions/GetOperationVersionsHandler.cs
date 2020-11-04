// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Operations.Versions
{
    public class GetOperationVersionsHandler : IRequestHandler<GetOperationVersionsRequest, GetOperationVersionsResponse>
    {
        private readonly IModelInfoProvider _provider;

        public GetOperationVersionsHandler(IModelInfoProvider provider)
        {
            EnsureArg.IsNotNull(provider, nameof(provider));

            _provider = provider;
        }

        Task<GetOperationVersionsResponse> IRequestHandler<GetOperationVersionsRequest, GetOperationVersionsResponse>.
            Handle(GetOperationVersionsRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handle(request));
        }

        protected GetOperationVersionsResponse Handle(GetOperationVersionsRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            // Truncating to "Major.Minor" version to follow format specified in the spec.
            var truncatedVersion = _provider.SupportedVersion.ToString(2);

            var supportedVersions = new List<string> { truncatedVersion };
            var defaultVersion = truncatedVersion;

            return new GetOperationVersionsResponse(supportedVersions, defaultVersion);
        }
    }
}

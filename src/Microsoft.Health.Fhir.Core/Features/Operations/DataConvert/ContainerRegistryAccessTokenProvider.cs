// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class ContainerRegistryAccessTokenProvider : IContainerRegistryTokenProvider
    {
        private readonly IAccessTokenProvider _aadTokenProvider;
        private readonly Uri _aadResourceUri = new Uri("https://management.core.windows.net/");

        public ContainerRegistryAccessTokenProvider(IAccessTokenProvider aadTokenProvider)
        {
            _aadTokenProvider = aadTokenProvider;
        }

        public async Task<string> GetTokenAsync(ContainerRegistryInfo containerRegistryInfo, CancellationToken cancellationToken)
        {
            var aadToken = await _aadTokenProvider.GetAccessTokenForResourceAsync(_aadResourceUri, cancellationToken);
            throw new System.NotImplementedException();
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public interface IContainerRegistryTokenProvider
    {
        public Task<string> GetTokenAsync(string registryServer, CancellationToken cancellationToken);
    }
}

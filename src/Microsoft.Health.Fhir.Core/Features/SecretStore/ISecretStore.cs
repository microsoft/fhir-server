// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.SecretStore
{
    public interface ISecretStore
    {
        Task<SecretWrapper> GetSecretAsync(string secretName, CancellationToken cancellationToken);

        Task<SecretWrapper> SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken);

        Task<SecretWrapper> DeleteSecretAsync(string secretName, CancellationToken cancellationToken);
    }
}

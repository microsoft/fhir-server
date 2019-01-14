// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    internal class CosmosIdentityProvider : IdentityProvider
    {
        public const string IdentityProviderPartition = "_identityProviders";

        public CosmosIdentityProvider(IdentityProvider identityProvider)
            : base(identityProvider?.Name, identityProvider?.Authority, identityProvider?.Audience)
        {
        }

        [JsonConstructor]
        protected CosmosIdentityProvider()
        {
        }

        [JsonProperty(KnownDocumentProperties.Id)]
        public string Id => Name;

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = IdentityProviderPartition;

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;

        [JsonProperty(KnownDocumentProperties.ETag)]
        public string ETag { get; protected set; }
    }
}

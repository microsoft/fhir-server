// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public class CosmosIdentityProvider : IdentityProvider
    {
        internal const string IdentityProviderPartition = "_identityProviders";

        public CosmosIdentityProvider(IdentityProvider identityProvider)
            : base(identityProvider.Name, identityProvider.Authority, identityProvider.Audience, identityProvider.Version)
        {
        }

        [JsonConstructor]
        protected CosmosIdentityProvider()
        {
        }

        [JsonProperty("id")]
        public string Id => Name;

        [JsonProperty("partitionKey")]
        public string PartitionKey { get; } = IdentityProviderPartition;

        [JsonProperty("_etag")]
        public virtual string ETag { get; protected set; }

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;

        public override string Version => ETag;
    }
}

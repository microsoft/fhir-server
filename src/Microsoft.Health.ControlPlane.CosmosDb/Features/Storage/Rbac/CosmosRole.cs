// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.ControlPlane.Core.Features.Rbac.Roles;
using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public class CosmosRole : Role
    {
        internal const string RolePartition = "Role_";

        public CosmosRole(Role role)
              : base(role.Name, role.ResourcePermissions,  role.Version)
        {
            ETag = $"\"{role.Version}\"";
        }

        [JsonConstructor]
        protected CosmosRole()
        {
        }

        [JsonProperty("id")]
        public string Id => Name;

        [JsonProperty("partitionKey")]
        public string PartitionKey { get; } = RolePartition;

        [JsonProperty("_etag")]
        public string ETag { get; protected set; }

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;

        public override string Version => ETag;
    }
}

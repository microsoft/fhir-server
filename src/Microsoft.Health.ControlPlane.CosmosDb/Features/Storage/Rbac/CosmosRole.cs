// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public class CosmosRole : Role
    {
        internal const string RolePartition = "_roles";

        public CosmosRole(Role role)
             : base(role.Name, role.ResourcePermissions)
        {
        }

        [JsonConstructor]
        protected CosmosRole()
        {
        }

        [JsonProperty(KnownDocumentProperties.Id)]
        public string Id => Name;

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = RolePartition;

        [JsonProperty(KnownDocumentProperties.ETag)]
        public string ETag { get; protected set; }

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Security
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

        [JsonProperty("id")]
        public string Id => Name;

        [JsonProperty("partitionKey")]
        public string PartitionKey { get; } = RolePartition;

        [JsonProperty("_etag")]
        public string ETag { get; protected set; }

        [JsonProperty(KnownResourceWrapperProperties.IsSystem)]
        public bool IsSystem { get; } = true;
    }
}

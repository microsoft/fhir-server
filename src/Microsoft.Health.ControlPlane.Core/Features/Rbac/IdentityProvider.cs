// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class IdentityProvider
    {
        [JsonConstructor]
        protected IdentityProvider()
        {
        }

        public IdentityProvider(string name, string authority, IReadOnlyList<string> audience, string version)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsNotNull(authority, nameof(authority));
            EnsureArg.IsNotNull(audience, nameof(audience));
            EnsureArg.IsNot(version, nameof(version));

            Name = name;
            Authority = authority;
            Audience = audience;
            Version = version;
        }

        [JsonProperty("name")]
        public string Name { get; protected set; }

        [JsonProperty("authority")]
        public string Authority { get; protected set; }

        [JsonProperty("audience")]
        public IReadOnlyList<string> Audience { get; protected set; }

        [JsonProperty("version")]
        public virtual string Version { get; protected set; }
    }
}

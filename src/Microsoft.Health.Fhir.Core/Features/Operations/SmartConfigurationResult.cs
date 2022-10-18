// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the smart configuration operation completes.
    /// </summary>
    public class SmartConfigurationResult
    {
        public SmartConfigurationResult(Uri authorizationEndpoint, Uri tokenEndpoint, ICollection<string> capabilities)
        {
            EnsureArg.IsNotNull(authorizationEndpoint, nameof(authorizationEndpoint));
            EnsureArg.IsNotNull(tokenEndpoint, nameof(tokenEndpoint));
            EnsureArg.IsNotNull(capabilities, nameof(capabilities));

            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            Capabilities = capabilities;
        }

        [JsonConstructor]
        public SmartConfigurationResult()
        {
        }

        [JsonProperty("authorizationEndpoint")]
        public Uri AuthorizationEndpoint { get; private set; }

        [JsonProperty("tokenEndpoint")]
        public Uri TokenEndpoint { get; private set; }

        [JsonProperty("capabilities")]
        public ICollection<string> Capabilities { get; private set; }
    }
}

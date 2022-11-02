// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetSmartConfigurationResponse
    {
        public GetSmartConfigurationResponse(Uri authorizationEndpoint, Uri tokenEndpoint, ICollection<string> capabilities)
        {
            EnsureArg.IsNotNull(authorizationEndpoint, nameof(authorizationEndpoint));
            EnsureArg.IsNotNull(tokenEndpoint, nameof(tokenEndpoint));
            EnsureArg.IsNotNull(capabilities, nameof(capabilities));

            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            Capabilities = capabilities;
        }

        public Uri AuthorizationEndpoint { get; }

        public Uri TokenEndpoint { get; }

        public ICollection<string> Capabilities { get; }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class SmartConfiguration
    {
        public SmartConfiguration(string baseEndpoint)
        {
            EnsureArg.IsNotNull(baseEndpoint, nameof(baseEndpoint));

            AuthorizationEndpoint = new Uri(string.Join(baseEndpoint, "/authorize"));
            TokenEndpoint = new Uri(string.Join(baseEndpoint, "/token"));
        }

        public Uri AuthorizationEndpoint { get; set; }

        public Uri TokenEndpoint { get; set; }

        public static ICollection<string> Capabilities { get; } = new List<string>
        {
            "launch-standalone",
            "client-public",
            "client-confidential-symmetric",
            "sso-openid-connect",
            "context-standalone-patient",
            "permission-offline",
            "permission-patient",
        };
    }
}

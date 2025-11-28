// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public class SmartV2InstantiateCapability : IInstantiateCapability
    {
        private static readonly string[] Urls =
        {
            "http://hl7.org/fhir/smart-app-launch/CapabilityStatement/smart-app-state-server",
        };

        private readonly SecurityConfiguration _securityConfiguration;

        public SmartV2InstantiateCapability(IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _securityConfiguration = securityConfiguration.Value;
        }

        public bool TryGetUrls(out IEnumerable<string> urls)
        {
            urls = null;
            if (_securityConfiguration.Authorization.Enabled
                || _securityConfiguration.Authorization.EnableSmartWithoutAuth)
            {
                urls = Urls;
                return true;
            }

            return false;
        }
    }
}

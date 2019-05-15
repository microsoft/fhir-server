// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class PrincipalClaimsExtractor : IClaimsExtractor
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly SecurityConfiguration _securityConfiguration;

        public PrincipalClaimsExtractor(IFhirRequestContextAccessor fhirRequestContextAccessor, IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _securityConfiguration = securityConfiguration.Value;
        }

        public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
        {
            return _fhirRequestContextAccessor.FhirRequestContext.Principal?.Claims?
                .Where(c => _securityConfiguration.PrincipalClaims?.Contains(c.Type) ?? false)
                .Select(c => new KeyValuePair<string, string>(c.Type, c.Value))
                .ToList();
        }
    }
}

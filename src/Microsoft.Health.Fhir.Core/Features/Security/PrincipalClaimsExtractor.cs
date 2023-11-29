// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class PrincipalClaimsExtractor : IClaimsExtractor
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly ISecurityConfiguration _securityConfiguration;

        public PrincipalClaimsExtractor(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor, IOptions<ISecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _securityConfiguration = securityConfiguration.Value;
        }

        public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
        {
            return _fhirRequestContextAccessor.RequestContext.Principal?.Claims?
                .Where(c => _securityConfiguration.PrincipalClaims?.Contains(c.Type) ?? false)
                .Select(c => new KeyValuePair<string, string>(c.Type, c.Value))
                .ToList();
        }
    }
}

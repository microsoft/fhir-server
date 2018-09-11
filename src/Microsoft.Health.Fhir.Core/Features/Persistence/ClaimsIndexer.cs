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

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ClaimsIndexer : IClaimsIndexer
    {
        private readonly IFhirContextAccessor _fhirContextAccessor;
        private readonly SecurityConfiguration _securityConfiguration;

        public ClaimsIndexer(IFhirContextAccessor fhirContextAccessor, IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));

            _fhirContextAccessor = fhirContextAccessor;
            _securityConfiguration = securityConfiguration.Value;
        }

        public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
        {
            return _fhirContextAccessor.FhirContext.Principal?.Claims?
                .Where(c => _securityConfiguration.LastModifiedClaims?.Contains(c.Type) ?? false)
                .Select(c => new KeyValuePair<string, string>(c.Type, c.Value))
                .ToList();
        }
    }
}

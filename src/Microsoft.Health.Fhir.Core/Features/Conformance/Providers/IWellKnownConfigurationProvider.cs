// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Providers
{
    public interface IWellKnownConfigurationProvider
    {
        bool IsSmartConfigured();

        Task<GetSmartConfigurationResponse> GetSmartConfigurationAsync(CancellationToken cancellationToken);

        Task<OpenIdConfigurationResponse> GetOpenIdConfigurationAsync(CancellationToken cancellationToken);
    }
}

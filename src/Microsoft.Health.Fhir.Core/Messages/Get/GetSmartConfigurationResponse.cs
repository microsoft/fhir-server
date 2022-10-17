// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetSmartConfigurationResponse
    {
        public GetSmartConfigurationResponse(ResourceElement smartConfiguration)
        {
            EnsureArg.IsNotNull(smartConfiguration, nameof(smartConfiguration));

            SmartConfiguration = smartConfiguration;
        }

        public ResourceElement SmartConfiguration { get; }
    }
}

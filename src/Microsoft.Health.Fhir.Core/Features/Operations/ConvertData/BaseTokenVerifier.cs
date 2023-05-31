// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class BaseTokenVerifier : IContainerRegistryTokenVerifier
    {
        public void CheckIfContainerRegistryAccessIsEnabled()
        {
            // base implementation - do nothing
        }
    }
}

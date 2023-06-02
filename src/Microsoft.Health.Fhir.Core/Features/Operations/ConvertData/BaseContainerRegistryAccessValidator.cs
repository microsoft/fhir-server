// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class BaseContainerRegistryAccessValidator : IContainerRegistryAccessValidator
    {
        public void CheckContainerRegistryAccess()
        {
            // base implementation - do nothing. If container registry access is not configured and not checked, a 400 error will be thrown further down the control flow, as expected.
        }
    }
}

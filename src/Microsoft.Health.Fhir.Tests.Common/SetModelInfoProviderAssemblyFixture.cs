// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// A temporary workaround to set the model info provider before any tests in an assembly are executed.
    /// Intended to used as an assembly test fixture.
    /// </summary>
    public class SetModelInfoProviderAssemblyFixture
    {
        public SetModelInfoProviderAssemblyFixture()
        {
            ModelExtensions.SetModelInfoProvider();
        }
    }
}

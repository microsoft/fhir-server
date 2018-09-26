// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Registration
{
    /// <summary>
    /// A builder type for configuring FHIR server services.
    /// </summary>
    public interface IFhirServerBuilder
    {
        IServiceCollection Services { get; }
    }
}

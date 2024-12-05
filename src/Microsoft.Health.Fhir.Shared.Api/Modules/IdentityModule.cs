// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Api.Modules;

public class IdentityModule : IStartupModule
{
    public void Load(IServiceCollection services)
    {
        // Provide support for Azure Managed Identity
        services.TryAddSingleton<TokenCredential, DefaultAzureCredential>();
    }
}

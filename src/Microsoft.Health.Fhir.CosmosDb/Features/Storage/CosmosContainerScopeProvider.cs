// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage;

/// <summary>
/// Creates caller-owned handles without transferring ownership of the shared Cosmos container.
/// </summary>
public sealed class CosmosContainerScopeProvider : IScopeProvider<Container>
{
    private readonly IServiceProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosContainerScopeProvider"/> class.
    /// </summary>
    /// <param name="provider">The provider from which to resolve the shared container's owner on first use.</param>
    public CosmosContainerScopeProvider(IServiceProvider provider)
    {
        _provider = EnsureArg.IsNotNull(provider, nameof(provider));
    }

    /// <inheritdoc />
    public IScoped<Container> Invoke() => _provider.GetRequiredService<CosmosContainerProvider>().CreateContainerScope();
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Scoping;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Scoping;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class OwnedScopeFactoryTests
{
    [Fact]
    public void GivenFactory_WhenCreatingScopes_ThenEachCallerOwnsAnIndependentLifetime()
    {
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        services.AddScoped(_ => Substitute.For<IDisposable>());
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IOwnedScopeFactory<IDisposable> factory = provider.GetRequiredService<IOwnedScopeFactory<IDisposable>>();

        using IOwnedScope<IDisposable> first = factory.Create();
        using IOwnedScope<IDisposable> second = factory.Create();
        IDisposable firstValue = first.Value;
        IDisposable secondValue = second.Value;
        first.Dispose();
        first.Dispose();

        Assert.NotSame(firstValue, secondValue);
        firstValue.Received(1).Dispose();
        secondValue.DidNotReceive().Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Value);
        Assert.Same(secondValue, second.Value);
    }

    [Fact]
    public void GivenRootResolvedFactory_WhenScopeIsDisposed_ThenRootDoesNotRetainHandle()
    {
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        services.AddScoped(_ => new object());
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        ScopedFactoryAssert.NotRetainedByRoot<object>(provider, nameof(Object));
    }

    [Fact]
    public void GivenFactoryResolvedInChildScope_WhenChildIsDisposed_ThenCreatedScopeStillBelongsToCaller()
    {
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        services.AddScoped(_ => Substitute.For<IDisposable>());
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IOwnedScope<IDisposable> owned;
        using (IServiceScope resolvingScope = provider.CreateScope())
        {
            owned = resolvingScope.ServiceProvider.GetRequiredService<IOwnedScopeFactory<IDisposable>>().Create();
        }

        using (owned)
        {
            owned.Value.DidNotReceive().Dispose();
        }

        Assert.Throws<ObjectDisposedException>(() => owned.Value);
    }

    [Fact]
    public void GivenMissingService_WhenCreatingScope_ThenResolutionFails()
    {
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IOwnedScopeFactory<IDisposable> factory = provider.GetRequiredService<IOwnedScopeFactory<IDisposable>>();

        Assert.Throws<InvalidOperationException>(() => factory.Create());
    }

    [Fact]
    public void GivenServiceActivationFailure_WhenCreatingScope_ThenAlreadyCreatedDependenciesAreDisposed()
    {
        var dependency = Substitute.For<IDisposable>();
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        services.AddScoped(_ => dependency);
        services.AddScoped<object>(provider =>
        {
            provider.GetRequiredService<IDisposable>();
            throw new InvalidOperationException("Activation failed.");
        });
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IOwnedScopeFactory<object> factory = provider.GetRequiredService<IOwnedScopeFactory<object>>();

        Assert.Throws<InvalidOperationException>(() => factory.Create());

        dependency.Received(1).Dispose();
    }

    [Fact]
    public void GivenRepeatedRegistration_WhenResolvingFactory_ThenOnlyOneFactoryIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddOwnedScopes();
        services.AddOwnedScopes();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IOwnedScopeFactory<object>>());
    }
}

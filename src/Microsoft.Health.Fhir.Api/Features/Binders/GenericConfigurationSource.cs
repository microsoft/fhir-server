// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

public class GenericConfigurationSource : IConfigurationSource
{
    private readonly Func<IConfigurationProvider> _instanceFactory;

    public GenericConfigurationSource(Func<IConfigurationProvider> instanceFactory)
    {
        _instanceFactory = EnsureArg.IsNotNull(instanceFactory, nameof(instanceFactory));
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return _instanceFactory.Invoke();
    }
}

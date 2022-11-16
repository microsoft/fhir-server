// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

public class DictionaryConfigurationSource<T> : IConfigurationSource
    where T : class, IConfigurationProvider, new()
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new T();
    }
}

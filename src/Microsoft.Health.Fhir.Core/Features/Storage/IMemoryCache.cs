// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Storage
{
    public interface IMemoryCache<T>
    {
        long CacheMemoryLimit { get; }

        T GetOrAdd(string key, T value);

        bool TryAdd(string key, T value);

        T Get(string key);

        bool TryGet(string key, out T value);

        bool Remove(string key);
    }
}

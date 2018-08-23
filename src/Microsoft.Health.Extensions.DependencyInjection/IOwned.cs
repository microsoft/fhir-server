// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Represents a dependency that can be released by the dependent component.
    /// </summary>
    /// <remarks>
    /// Many full-feature containers implement this concept, a usecase might be as follows; A singleton needs to resolved a scoped component,
    /// this case is not possible as lifetimes would be mixed (singleton is higher than scoped).
    /// In this special case, the Singleton component can request control of the sub-component lifetime via the Owned class.
    /// </remarks>
    /// <typeparam name="T">Component type to resolve</typeparam>
    public interface IOwned<T> : IDisposable
    {
        T Value { get; }
    }
}

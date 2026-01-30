// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Xunit.Abstractions
{
    /// <summary>
    /// Compatibility shim for legacy Xunit.Abstractions usage.
    /// </summary>
    public interface ITestOutputHelper : Xunit.ITestOutputHelper
    {
    }
}

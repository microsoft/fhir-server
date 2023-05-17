// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="RawResource"/>
    /// </summary>
    public interface IRawResourceFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="RawResource"/>.
        /// </summary>
        /// <param name="resource">The resource to be converted</param>
        /// <param name="keepMeta">Keep meta section if true, remove if false.</param>
        /// <param name="keepVersion">Keeps version id if true, resets to 1 if false.</param>
        /// <returns>An instance of <see cref="RawResource"/>.</returns>
        RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false);
    }
}

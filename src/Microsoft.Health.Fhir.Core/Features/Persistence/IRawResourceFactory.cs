// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
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
        /// <returns>An instance of <see cref="RawResource"/>.</returns>
        RawResource Create(ResourceElement resource);
    }
}

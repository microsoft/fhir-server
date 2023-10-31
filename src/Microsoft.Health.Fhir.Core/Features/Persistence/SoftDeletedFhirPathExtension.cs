// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public static class SoftDeletedFhirPathExtension
{
    /// <summary>
    /// Return true if this resource contains the Azure 'soft-deleted' extension in meta data
    /// </summary>
    public static bool IsSoftDeleted(this ResourceElement resourceElement)
    {
        return resourceElement.Predicate(KnownFhirPaths.IsSoftDeletedExtension);
    }
}

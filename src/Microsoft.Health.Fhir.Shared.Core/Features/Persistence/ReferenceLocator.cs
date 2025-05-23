// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Gets the FHIR path to a reference given the reference's resource type and the resource type of the resource that contains the reference.
    /// </summary>
    public partial class ReferenceLocator : IReferenceLocator
    {
        private readonly Dictionary<string, Dictionary<string, IEnumerable<string>>> _referenceLocations;

        public IEnumerable<string> GetReferenceLocation(string resourceType, string referenceType)
        {
            if (!_referenceLocations.TryGetValue(resourceType, out var resourcePaths))
            {
                return null;
            }

            if (!resourcePaths.TryGetValue(referenceType, out var referenceLocations))
            {
                return null;
            }

            return referenceLocations;
        }
    }
}

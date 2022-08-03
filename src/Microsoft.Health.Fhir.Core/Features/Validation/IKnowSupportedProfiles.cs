// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface IKnowSupportedProfiles
    {
        /// <summary>
        /// Provide supported profiles for specified <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">Resource type to get profiles.</param>
        /// <param name="disableCacheRefresh">Should we check server for new updates or get data out of cache.</param>
        IEnumerable<string> GetSupportedProfiles(string resourceType, bool disableCacheRefresh = false);

        void Refresh();
    }
}

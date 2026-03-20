// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    /// <summary>
    /// A notification that is raised when the SearchParameter cache has been successfully refreshed
    /// by the background service. This allows consumers (such as reindex jobs) to deterministically
    /// wait for cache refresh cycles instead of relying on passive time-based delays.
    /// </summary>
    public class SearchParameterCacheRefreshedNotification : INotification
    {
    }
}

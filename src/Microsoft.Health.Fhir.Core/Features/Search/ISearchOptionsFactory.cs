// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface ISearchOptionsFactory
    {
        /// <summary>
        /// Creates <see cref="SearchOptions"/> for a resource type search.
        /// </summary>
        /// <param name="resourceType">The resource type being searched, or <see langword="null"/> for a system-level search.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="isAsyncOperation">Whether the search is part of an async operation.</param>
        /// <param name="resourceVersionTypes">Which version types (latest, soft-deleted, history) to include in search.</param>
        /// <param name="onlyIds">Whether to return only the resource ids, not the full resource.</param>
        /// <param name="isIncludesOperation">Whether the search is to query remaining include resources.</param>
        /// <param name="scopeDataActions">
        /// The data actions that may authorize the search. Only SMART scopes granting one of these actions are applied.
        /// Defaults to search (<see cref="DataActions.Read"/> | <see cref="DataActions.Search"/>); direct reads by id pass
        /// <see cref="DataActions.Read"/> | <see cref="DataActions.ReadById"/>.
        /// </param>
        /// <returns>The created <see cref="SearchOptions"/>.</returns>
        SearchOptions Create(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false,
            DataActions scopeDataActions = DataActions.Read | DataActions.Search);

        SearchOptions Create(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            bool isAsyncOperation = false,
            bool useSmartCompartmentDefinition = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false);
    }
}

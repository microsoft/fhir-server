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
        SearchOptions Create(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false);

        /// <summary>
        /// Creates <see cref="SearchOptions"/> using only SMART scope restrictions that permit one of the supplied actions.
        /// </summary>
        /// <remarks>
        /// The default interface implementation delegates to the overload that does not take <paramref name="scopeDataActions"/>,
        /// so implementations should override this method to enforce action-specific scope filtering.
        /// </remarks>
        SearchOptions Create(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            DataActions scopeDataActions,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false)
        {
            return Create(resourceType, queryParameters, isAsyncOperation, resourceVersionTypes, onlyIds, isIncludesOperation);
        }

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

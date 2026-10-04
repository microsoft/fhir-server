// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceSearchParamListRowGenerator : MergeSearchParameterRowGenerator<ReferenceSearchValue, ReferenceSearchParamListRow>
    {
        private readonly int _maxLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchParamListRowGenerator"/> class.
        /// </summary>
        /// <param name="model">The SQL Server FHIR model.</param>
        /// <param name="searchParameterTypeMap">The search parameter type map.</param>
        /// <param name="config">The core feature configuration.</param>
        public ReferenceSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap, IOptions<CoreFeatureConfiguration> config)
            : base(model, searchParameterTypeMap)
        {
            _maxLength = EnsureArg.IsNotNull(config, nameof(config)).Value.MaxResourceIdLength;
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceRecordId, short searchParamId, ReferenceSearchValue searchValue, HashSet<ReferenceSearchParamListRow> results, out ReferenceSearchParamListRow row)
        {
            row = new ReferenceSearchParamListRow(
                resourceTypeId,
                resourceRecordId,
                searchParamId,
                searchValue.BaseUri?.ToString(),
                searchValue.ResourceType == null ? null : Model.GetResourceTypeId(searchValue.ResourceType),
                searchValue.ResourceId[..Math.Min(searchValue.ResourceId.Length, _maxLength)], // Truncate to fit the column size. TODO: We should separate string references (ref resource type is null) from references to resources. This should be a long term fix.
                ReferenceResourceVersion: null);

            return results == null || results.Add(row);
        }
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable SA1402 // Keep the resource ID column helper and TVP definitions together.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal static class ResourceIdColumn
    {
        internal static Column Widen(Column column, int maxResourceIdLength)
        {
            if (column is VarCharColumn stringColumn
                && column.Metadata.Name is "ResourceId" or "ReferenceResourceId" or "ReferenceResourceId1"
                && maxResourceIdLength > column.Metadata.MaxLength)
            {
                return new VarCharColumn(column.Metadata.Name, maxResourceIdLength, stringColumn.Collation);
            }

            return column;
        }
    }

    internal sealed class WideResourceListTableValuedParameterDefinition : ResourceListTableValuedParameterDefinition
    {
        private readonly int _maxResourceIdLength;

        internal WideResourceListTableValuedParameterDefinition(string parameterName, int maxResourceIdLength)
            : base(parameterName)
        {
            _maxResourceIdLength = maxResourceIdLength;
        }

        protected override IEnumerable<Column> Columns => base.Columns.Select(column => ResourceIdColumn.Widen(column, _maxResourceIdLength));
    }

    internal sealed class WideReferenceSearchParamListTableValuedParameterDefinition : ReferenceSearchParamListTableValuedParameterDefinition
    {
        private readonly int _maxResourceIdLength;

        internal WideReferenceSearchParamListTableValuedParameterDefinition(string parameterName, int maxResourceIdLength)
            : base(parameterName)
        {
            _maxResourceIdLength = maxResourceIdLength;
        }

        protected override IEnumerable<Column> Columns => base.Columns.Select(column => ResourceIdColumn.Widen(column, _maxResourceIdLength));
    }

    internal sealed class WideReferenceTokenCompositeSearchParamListTableValuedParameterDefinition : ReferenceTokenCompositeSearchParamListTableValuedParameterDefinition
    {
        private readonly int _maxResourceIdLength;

        internal WideReferenceTokenCompositeSearchParamListTableValuedParameterDefinition(string parameterName, int maxResourceIdLength)
            : base(parameterName)
        {
            _maxResourceIdLength = maxResourceIdLength;
        }

        protected override IEnumerable<Column> Columns => base.Columns.Select(column => ResourceIdColumn.Widen(column, _maxResourceIdLength));
    }

    internal sealed class WideResourceKeyListTableValuedParameterDefinition : ResourceKeyListTableValuedParameterDefinition
    {
        private readonly int _maxResourceIdLength;

        internal WideResourceKeyListTableValuedParameterDefinition(string parameterName, int maxResourceIdLength)
            : base(parameterName)
        {
            _maxResourceIdLength = maxResourceIdLength;
        }

        protected override IEnumerable<Column> Columns => base.Columns.Select(column => ResourceIdColumn.Widen(column, _maxResourceIdLength));
    }

    internal sealed class WideResourceDateKeyListTableValuedParameterDefinition : ResourceDateKeyListTableValuedParameterDefinition
    {
        private readonly int _maxResourceIdLength;

        internal WideResourceDateKeyListTableValuedParameterDefinition(string parameterName, int maxResourceIdLength)
            : base(parameterName)
        {
            _maxResourceIdLength = maxResourceIdLength;
        }

        protected override IEnumerable<Column> Columns => base.Columns.Select(column => ResourceIdColumn.Widen(column, _maxResourceIdLength));
    }
}

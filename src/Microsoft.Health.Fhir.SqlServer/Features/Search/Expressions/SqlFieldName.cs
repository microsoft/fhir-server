// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    public static class SqlFieldName
    {
        public const FieldName ResourceSurrogateId = (FieldName)100;
        public const FieldName ResourceTypeId = (FieldName)101;
        public const FieldName LastUpdated = (FieldName)102;
    }
}

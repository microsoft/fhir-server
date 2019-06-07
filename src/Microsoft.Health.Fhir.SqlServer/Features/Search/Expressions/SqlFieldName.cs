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
        public const FieldName LastUpdated = (FieldName)101;
        public const FieldName TextOverflow = (FieldName)102;
        public const FieldName NumberLow = (FieldName)103;
        public const FieldName NumberHigh = (FieldName)104;
        public const FieldName QuantityLow = (FieldName)105;
        public const FieldName QuantityHigh = (FieldName)106;
        public const FieldName DateTimeIsLongerThanADay = (FieldName)107;
    }
}

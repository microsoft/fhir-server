// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.NormalizedTableHandlers
{
#pragma warning disable SA1402 // File may only contain a single type

    internal abstract class NormalizedTableHandler : SearchParameterQueryGenerator
    {
        public abstract Table Table { get; }
    }

    internal class CompartmentNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly CompartmentNormalizedTableHandler Instance = new CompartmentNormalizedTableHandler();

        public override Table Table => V1.CompartmentAssignment;

        public override SqlQueryGenerator VisitCompartment(CompartmentSearchExpression expression, SqlQueryGenerator context)
        {
            byte compartmentId = context.Model.GetCompartmentId(expression.CompartmentType);

            context.StringBuilder
                .Append(V1.CompartmentAssignment.CompartmentTypeId)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(V1.CompartmentAssignment.CompartmentTypeId, compartmentId))
                .AppendLine()
                .Append("AND ")
                .Append(V1.CompartmentAssignment.ReferenceResourceId)
                .Append(" = ")
                .Append(context.Parameters.AddParameter(V1.CompartmentAssignment.ReferenceResourceId, expression.CompartmentId));

            return context;
        }
    }

    internal class TokenNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly TokenNormalizedTableHandler Instance = new TokenNormalizedTableHandler();

        public override Table Table => V1.TokenSearchParam;

        public override SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
        {
            if (expression.FieldName != FieldName.TokenSystem)
            {
                throw new InvalidOperationException($"Unexpected missing field {expression.FieldName}");
            }

            context.StringBuilder.Append(V1.TokenSearchParam.SystemId).Append(" IS NULL");
            return context;
        }

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            Debug.Assert(expression.StringOperator == StringOperator.Equals, "Only equals is supported");

            switch (expression.FieldName)
            {
                case FieldName.TokenSystem:
                    VisitSimpleBinary(BinaryOperator.Equal, context, V1.TokenSearchParam.SystemId, context.Model.GetSystem(expression.Value));
                    break;
                case FieldName.TokenCode:
                    VisitSimpleString(expression, context, V1.TokenSearchParam.Code, expression.Value);
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return context;
        }
    }

    internal class TokenTextNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly TokenTextNormalizedTableHandler Instance = new TokenTextNormalizedTableHandler();

        public override Table Table => V1.TokenText;
    }

    internal class DateNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly DateNormalizedTableHandler Instance = new DateNormalizedTableHandler();

        public override Table Table => V1.DateTimeSearchParam;
    }

    internal class NumberNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly NumberNormalizedTableHandler Instance = new NumberNormalizedTableHandler();

        public override Table Table => V1.NumberSearchParam;
    }

    internal class QuantityNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly QuantityNormalizedTableHandler Instance = new QuantityNormalizedTableHandler();

        public override Table Table => V1.QuantitySearchParam;
    }

    internal class ReferenceNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly ReferenceNormalizedTableHandler Instance = new ReferenceNormalizedTableHandler();

        public override Table Table => V1.ReferenceSearchParam;
    }

    internal class StringNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly StringNormalizedTableHandler Instance = new StringNormalizedTableHandler();

        public override Table Table => V1.StringSearchParam;
    }

    internal class UriNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly UriNormalizedTableHandler Instance = new UriNormalizedTableHandler();

        public override Table Table => V1.UriSearchParam;
    }

#pragma warning restore SA1402 // File may only contain a single type
}

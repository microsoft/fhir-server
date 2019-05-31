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
                    VisitSimpleBinary(BinaryOperator.Equal, context, V1.TokenSearchParam.SystemId, expression.ComponentIndex, context.Model.GetSystem(expression.Value));
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

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            return VisitSimpleString(expression, context, V1.TokenText.Text, expression.Value);
        }
    }

    internal class DateNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly DateNormalizedTableHandler Instance = new DateNormalizedTableHandler();

        public override Table Table => V1.DateTimeSearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            DateTime2Column column;
            switch (expression.FieldName)
            {
                case FieldName.DateTimeStart:
                    column = V1.DateTimeSearchParam.StartDateTime;
                    break;
                case FieldName.DateTimeEnd:
                    column = V1.DateTimeSearchParam.EndDateTime;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            return VisitSimpleBinary(expression.BinaryOperator, context, column, expression.ComponentIndex, expression.Value);
        }
    }

    internal class NumberNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly NumberNormalizedTableHandler Instance = new NumberNormalizedTableHandler();

        public override Table Table => V1.NumberSearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            var column = V1.NumberSearchParam.SingleValue;
            context.StringBuilder.Append(column).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, column, expression.ComponentIndex, expression.Value);
        }
    }

    internal class QuantityNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly QuantityNormalizedTableHandler Instance = new QuantityNormalizedTableHandler();

        public override Table Table => V1.QuantitySearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            var column = V1.QuantitySearchParam.SingleValue;
            context.StringBuilder.Append(column).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, column, expression.ComponentIndex, expression.Value);
        }

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            switch (expression.FieldName)
            {
                case FieldName.QuantityCode:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.QuantityCodeId, expression.ComponentIndex, context.Model.GetQuantityCode(expression.Value));
                case FieldName.QuantitySystem:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.SystemId, expression.ComponentIndex, context.Model.GetSystem(expression.Value));
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }
    }

    internal class ReferenceNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly ReferenceNormalizedTableHandler Instance = new ReferenceNormalizedTableHandler();

        public override Table Table => V1.ReferenceSearchParam;

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            switch (expression.FieldName)
            {
                case FieldName.ReferenceBaseUri:
                    return VisitSimpleString(expression, context, V1.ReferenceSearchParam.BaseUri, expression.Value);
                case FieldName.ReferenceResourceType:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, V1.ReferenceSearchParam.ReferenceResourceTypeId, expression.ComponentIndex, context.Model.GetResourceTypeId(expression.Value));
                case FieldName.ReferenceResourceId:
                    return VisitSimpleString(expression, context, V1.ReferenceSearchParam.ReferenceResourceId, expression.Value);
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }

        public override SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
        {
            return VisitMissingFieldImpl(expression, context, FieldName.ReferenceBaseUri, V1.ReferenceSearchParam.BaseUri);
        }
    }

    internal class StringNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly StringNormalizedTableHandler Instance = new StringNormalizedTableHandler();

        public override Table Table => V1.StringSearchParam;

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            context.StringBuilder.Append(V1.StringSearchParam.TextOverflow).Append(expression.ComponentIndex + 1).Append(" IS NULL AND ");
            return VisitSimpleString(expression, context, V1.StringSearchParam.Text, expression.Value);
        }
    }

    internal class UriNormalizedTableHandler : NormalizedTableHandler
    {
        public static readonly UriNormalizedTableHandler Instance = new UriNormalizedTableHandler();

        public override Table Table => V1.UriSearchParam;

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            return VisitSimpleString(expression, context, V1.UriSearchParam.Uri, expression.Value);
        }
    }

    internal abstract class CompositeNormalizedTableHandler : NormalizedTableHandler
    {
        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            return expression.AcceptVisitor(GetComponentHandler((int)expression.ComponentIndex), context);
        }

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            return expression.AcceptVisitor(GetComponentHandler((int)expression.ComponentIndex), context);
        }

        public override SqlQueryGenerator VisitMissingField(MissingFieldExpression expression, SqlQueryGenerator context)
        {
            return expression.AcceptVisitor(GetComponentHandler((int)expression.ComponentIndex), context);
        }

        protected abstract NormalizedTableHandler GetComponentHandler(int componentIndex);
    }

    internal class TokenQuantityCompositeNormalizedTableHandler : CompositeNormalizedTableHandler
    {
        public static readonly TokenQuantityCompositeNormalizedTableHandler Instance = new TokenQuantityCompositeNormalizedTableHandler();

        public override Table Table => V1.TokenQuantityCompositeSearchParam;

        protected override NormalizedTableHandler GetComponentHandler(int componentIndex)
        {
            return componentIndex == 0 ? (NormalizedTableHandler)TokenNormalizedTableHandler.Instance : QuantityNormalizedTableHandler.Instance;
        }
    }

#pragma warning restore SA1402 // File may only contain a single type
}

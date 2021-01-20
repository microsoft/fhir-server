// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class UriQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly UriQueryGenerator Instance = new UriQueryGenerator();

        public override Table Table => VLatest.UriSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.Uri:
                    return VisitSimpleString(expression, context, VLatest.UriSearchParam.Uri, expression.Value);
                case FieldName.UriVersion:
                    return VisitSimpleString(expression, context, VLatest.UriSearchParam.Version, expression.Value);
                case FieldName.UriFragment:
                    return VisitSimpleString(expression, context, VLatest.UriSearchParam.Fragment, expression.Value);
                default:
                    throw new NotSupportedException();
            }
        }

        public override SearchParameterQueryGeneratorContext VisitMissingField(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.UriVersion:
                    return VisitMissingFieldImpl(expression, context, FieldName.UriVersion, VLatest.UriSearchParam.Fragment);
                case FieldName.UriFragment:
                    return VisitMissingFieldImpl(expression, context, FieldName.UriFragment, VLatest.UriSearchParam.Fragment);
                default:
                    throw new NotSupportedException();
            }
        }
    }
}

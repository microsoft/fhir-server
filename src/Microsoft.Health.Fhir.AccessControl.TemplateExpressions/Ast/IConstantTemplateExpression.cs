// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// Represents a AST node that is a constant.
    /// </summary>
    internal interface IConstantTemplateExpression
    {
        object Value { get; }
    }
}

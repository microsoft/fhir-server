// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Holds <see cref="FunctionMetadata"/> for each of the functions available to template expressions.
    /// </summary>
    internal class TemplateExpressionFunctionRepository
    {
        public TemplateExpressionFunctionRepository(IEnumerable<ITemplateExpressionFunction> functions)
        {
            EnsureArg.IsNotNull(functions, nameof(functions));

            Functions = functions.ToDictionary(f => f.Name, f => new FunctionMetadata(f), StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, FunctionMetadata> Functions { get; }
    }
}

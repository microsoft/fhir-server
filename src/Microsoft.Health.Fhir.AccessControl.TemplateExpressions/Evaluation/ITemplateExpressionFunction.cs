// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    public interface ITemplateExpressionFunction
    {
        /// <summary>
        /// The function name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A delegate to call the function. Injected parameters should specify the <see cref="InjectedAttribute"/>
        /// and the return type cannot be void.
        /// </summary>
        Delegate Delegate { get; }
    }
}

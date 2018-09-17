// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Specifies on parameters of <see cref="ITemplateExpressionFunction.Delegate"/> where the value is to be provided by the dependency injection subsystem.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class InjectedAttribute : Attribute
    {
    }
}

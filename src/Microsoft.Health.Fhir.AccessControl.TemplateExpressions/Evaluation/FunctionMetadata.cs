// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using EnsureThat;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Validates and provides metadata about <see cref="ITemplateExpressionFunction"/>s.
    /// </summary>
    internal class FunctionMetadata
    {
        private readonly ITemplateExpressionFunction _function;

        public FunctionMetadata(ITemplateExpressionFunction function)
        {
            EnsureArg.IsNotNull(function, nameof(function));
            _function = function;

            if (function.Delegate == null)
            {
                throw new ArgumentException($"{nameof(ITemplateExpressionFunction)}.{nameof(Delegate)} cannot be null");
            }

            MethodInfo = function.Delegate.GetType().GetMethod("Invoke");
            AllParameters = MethodInfo.GetParameters();

            if (function.Delegate.Target == null)
            {
                // we can optimize calls to call the target method directly if the method is static/there is no instance parameter.
                MethodInfo = function.Delegate.Method;
            }
            else
            {
                Target = function.Delegate;
            }

            if (string.IsNullOrWhiteSpace(function.Name))
            {
                throw new ArgumentException($"{nameof(ITemplateExpressionFunction)}.{function.Name} cannot be null or empty.");
            }

            if (ReturnType == typeof(void))
            {
                throw new ArgumentException($"Function '{Name}' must have a non-void return type.");
            }

            ExposedParameters = AllParameters.Where(p => p.GetCustomAttribute<InjectedAttribute>() == null).ToArray();
        }

        /// <summary>
        /// The delegate's Invoke method, or the method it points to, if it is static.
        /// </summary>
        public MethodInfo MethodInfo { get; }

        /// <summary>
        /// The delegate's target (this) instance.
        /// </summary>
        public object Target { get; }

        /// <summary>
        /// The function's name.
        /// </summary>
        public string Name => _function.Name;

        /// <summary>
        /// The function's return type.
        /// </summary>
        public Type ReturnType => _function.Delegate.Method.ReturnType;

        /// <summary>
        /// The function's parameters.
        /// </summary>
        public ParameterInfo[] AllParameters { get; }

        /// <summary>
        /// The function's parameters except the hidden IServiceProvider parameter.
        /// </summary>
        public ParameterInfo[] ExposedParameters { get; }

        /// <summary>
        /// THe function's delegate.
        /// </summary>
        public Delegate Delegate => _function.Delegate;
    }
}

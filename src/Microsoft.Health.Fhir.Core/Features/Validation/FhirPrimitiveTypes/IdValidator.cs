// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using FluentValidation;
using FluentValidation.Validators;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// Validates a resource Id based on rules from https://www.hl7.org/fhir/datatypes.html
    /// </summary>
    /// <typeparam name="T">The type of the element.</typeparam>
    public class IdValidator<T> : PropertyValidator<T, string>
    {
        private readonly ResourceIdPolicy _policy;

        /// <summary>
        /// Initializes a new instance of the <see cref="IdValidator{T}"/> class.
        /// </summary>
        /// <param name="policy">The resource id policy.</param>
        public IdValidator(ResourceIdPolicy policy)
        {
            _policy = EnsureArg.IsNotNull(policy, nameof(policy));
        }

        /// <inheritdoc />
        public override string Name => "IdValidator";

        /// <inheritdoc />
        public override bool IsValid(ValidationContext<T> context, string value)
            => value == null || _policy.IsValid(value);

        /// <inheritdoc />
        protected override string GetDefaultMessageTemplate(string errorCode)
            => "'{PropertyName}' is not in the correct format.";
    }
}

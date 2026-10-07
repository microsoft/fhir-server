// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
        private readonly bool _useLongResourceIds;

        /// <summary>
        /// Initializes a new instance of the <see cref="IdValidator{T}"/> class.
        /// </summary>
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        public IdValidator(bool useLongResourceIds = false)
        {
            _useLongResourceIds = useLongResourceIds;
        }

        /// <inheritdoc />
        public override string Name => "IdValidator";

        /// <inheritdoc />
        public override bool IsValid(ValidationContext<T> context, string value)
            => value == null || ResourceIdValidation.IsValid(value, _useLongResourceIds);

        /// <inheritdoc />
        protected override string GetDefaultMessageTemplate(string errorCode)
            => "'{PropertyName}' is not in the correct format.";
    }
}

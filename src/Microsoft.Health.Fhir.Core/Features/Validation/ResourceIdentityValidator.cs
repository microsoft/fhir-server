// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Validates a resource id and every typed reference id against the active <see cref="ResourceIdPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Every <c>Reference</c> element is visited, whether or not a search parameter indexes it, and is recognized by the
    /// same converter that indexing uses so admission and indexing cannot disagree about what a reference identity is.
    /// </remarks>
    public sealed class ResourceIdentityValidator : IResourceIdentityValidator
    {
        private const string ReferenceType = "Reference";
        private const string ReferenceElementName = "reference";
        private const string ResourceIdPropertyName = "Resource.id";

        private readonly ResourceIdPolicy _resourceIdPolicy;
        private readonly ResourceReferenceToReferenceSearchValueConverter _referenceConverter;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceIdentityValidator"/> class.
        /// </summary>
        /// <param name="resourceIdPolicy">The resource id policy.</param>
        /// <param name="referenceSearchValueParser">The parser that owns reference identity recognition.</param>
        public ResourceIdentityValidator(ResourceIdPolicy resourceIdPolicy, IReferenceSearchValueParser referenceSearchValueParser)
        {
            _resourceIdPolicy = EnsureArg.IsNotNull(resourceIdPolicy, nameof(resourceIdPolicy));
            _referenceConverter = new ResourceReferenceToReferenceSearchValueConverter(
                EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser)));
        }

        /// <inheritdoc />
        public IReadOnlyList<ValidationFailure> Validate(ResourceElement resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            List<ValidationFailure> failures = null;

            string id = resource.Id;
            if (id != null && !_resourceIdPolicy.IsValid(id))
            {
                failures = new List<ValidationFailure>
                {
                    new ValidationFailure(
                        ResourceIdPropertyName,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, _resourceIdPolicy.MaxLength)),
                };
            }

            var pending = new Stack<ITypedElement>();
            pending.Push(resource.Instance);

            while (pending.Count > 0)
            {
                ITypedElement node = pending.Pop();

                if (string.Equals(node.InstanceType, ReferenceType, StringComparison.Ordinal))
                {
                    ValidationFailure failure = ValidateReference(node);
                    if (failure != null)
                    {
                        failures ??= new List<ValidationFailure>();
                        failures.Add(failure);
                    }
                }

                foreach (ITypedElement child in node.Children())
                {
                    pending.Push(child);
                }
            }

            return (IReadOnlyList<ValidationFailure>)failures ?? Array.Empty<ValidationFailure>();
        }

        private ValidationFailure ValidateReference(ITypedElement reference)
        {
            if (string.IsNullOrWhiteSpace(reference.Children(ReferenceElementName).FirstOrDefault()?.Value as string))
            {
                // Identifier-only and blank references carry no identity, and blank ones are left to content validation.
                return null;
            }

            try
            {
                // Enumerating runs the parser, which rejects an identity the policy cannot hold whole.
                _ = _referenceConverter.ConvertTo(reference).Count();
                return null;
            }
            catch (ResourceIdentityFormatException ex)
            {
                // The location is index based, so neither the property name nor the message carries the submitted value.
                return new ValidationFailure(reference.Location, ex.Message);
            }
        }
    }
}

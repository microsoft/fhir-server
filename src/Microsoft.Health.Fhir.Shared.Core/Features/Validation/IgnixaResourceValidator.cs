// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Resource validator using Ignixa.Validation for fast-path validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This validator implements a tiered validation strategy:
    /// </para>
    /// <list type="bullet">
    /// <item><description>For Ignixa resources: Uses Ignixa.Validation with configurable depth (Minimal/Spec) for ~1-5ms validation</description></item>
    /// <item><description>For non-Ignixa resources: Falls back to Firely DotNetAttributeValidation for compatibility</description></item>
    /// </list>
    /// <para>
    /// The validator caches compiled validation schemas per resource type for optimal performance.
    /// </para>
    /// </remarks>
    public sealed class IgnixaResourceValidator : IModelAttributeValidator
    {
        private readonly IIgnixaSchemaContext _schemaContext;
        private readonly ModelAttributeValidator _fallbackValidator;
        private readonly ConcurrentDictionary<string, ValidationSchema> _schemaCache;
        private readonly StructureDefinitionSchemaBuilder _schemaBuilder;
        private readonly ValidationSettings _validationSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaResourceValidator"/> class.
        /// </summary>
        /// <param name="schemaContext">The Ignixa schema context providing type definitions.</param>
        /// <param name="fallbackValidator">The fallback validator for non-Ignixa resources.</param>
        public IgnixaResourceValidator(
            IIgnixaSchemaContext schemaContext,
            ModelAttributeValidator fallbackValidator)
        {
            EnsureArg.IsNotNull(schemaContext, nameof(schemaContext));
            EnsureArg.IsNotNull(fallbackValidator, nameof(fallbackValidator));

            _schemaContext = schemaContext;
            _fallbackValidator = fallbackValidator;
            _schemaCache = new ConcurrentDictionary<string, ValidationSchema>(StringComparer.OrdinalIgnoreCase);
            _schemaBuilder = new StructureDefinitionSchemaBuilder();

            // Configure fast-mode validation (Tier 1-2: structure + cardinality + types)
            // Skip terminology validation for performance in the critical path
            _validationSettings = new ValidationSettings
            {
                Depth = ValidationDepth.Spec,
                SkipTerminologyValidation = true,
            };
        }

        /// <inheritdoc />
        public bool TryValidate(ResourceElement value, ICollection<DataAnnotations.ValidationResult> validationResults = null, bool recurse = false)
        {
            EnsureArg.IsNotNull(value, nameof(value));

            // Check if this is an Ignixa resource
            var ignixaNode = value.GetIgnixaNode();
            if (ignixaNode == null)
            {
                // Fall back to Firely validation for non-Ignixa resources
                return _fallbackValidator.TryValidate(value, validationResults, recurse);
            }

            // Use Ignixa fast-path validation
            return TryValidateIgnixa(ignixaNode, value.InstanceType, validationResults);
        }

        /// <summary>
        /// Validates an Ignixa resource using the fast-path validation pipeline.
        /// </summary>
        /// <param name="resourceNode">The Ignixa resource node to validate.</param>
        /// <param name="resourceType">The FHIR resource type name.</param>
        /// <param name="validationResults">Optional collection to receive validation results.</param>
        /// <returns>True if validation passed; otherwise false.</returns>
        private bool TryValidateIgnixa(
            ResourceJsonNode resourceNode,
            string resourceType,
            ICollection<DataAnnotations.ValidationResult> validationResults)
        {
            // Get or build the validation schema for this resource type
            var schema = GetOrBuildSchema(resourceType);
            if (schema == null)
            {
                // No schema available - skip validation (pass by default)
                // This can happen for unsupported or unknown resource types
                return true;
            }

            // Convert ResourceJsonNode to IElement for validation
            var element = resourceNode.ToElement(_schemaContext.Schema);

            // Initialize validation state
            var state = new ValidationState()
                .WithInstance(resourceType, resourceNode.Id);

            // Execute validation
            var result = schema.Validate(element, _validationSettings, state);

            // Convert issues to ValidationResult if collection is provided
            if (validationResults != null)
            {
                foreach (var issue in result.Issues)
                {
                    if (issue.Severity == IssueSeverity.Error || issue.Severity == IssueSeverity.Fatal)
                    {
                        var memberNames = string.IsNullOrEmpty(issue.Path)
                            ? null
                            : new[] { issue.Path };

                        validationResults.Add(new DataAnnotations.ValidationResult(issue.Message, memberNames));
                    }
                }
            }

            return result.IsValid;
        }

        /// <summary>
        /// Gets or builds a cached validation schema for the specified resource type.
        /// </summary>
        /// <param name="resourceType">The FHIR resource type name.</param>
        /// <returns>The validation schema, or null if the type is not found.</returns>
        private ValidationSchema GetOrBuildSchema(string resourceType)
        {
            return _schemaCache.GetOrAdd(resourceType, type =>
            {
                var typeDefinition = _schemaContext.Schema.GetTypeDefinition(type);
                if (typeDefinition == null)
                {
                    // Return null marker - we'll handle this in the caller
                    return null;
                }

                return _schemaBuilder.BuildSchema(
                    typeDefinition,
                    _schemaContext.Schema,
                    terminologyService: null); // No terminology for fast-path
            });
        }
    }
}

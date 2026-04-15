// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlOnFhir;

/// <summary>
/// Evaluates SQL on FHIR v2 ViewDefinitions against FHIR resources.
/// Bridges the FHIR server's Firely SDK resource model with the Ignixa SQL on FHIR evaluator.
/// </summary>
public interface IViewDefinitionEvaluator
{
    /// <summary>
    /// Evaluates a ViewDefinition (as JSON) against a single FHIR resource.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="resource">The FHIR resource to evaluate against.</param>
    /// <returns>The evaluation result containing rows.</returns>
    ViewDefinitionResult Evaluate(string viewDefinitionJson, ResourceElement resource);

    /// <summary>
    /// Evaluates a ViewDefinition (as JSON) against a single FHIR resource provided as an <see cref="ITypedElement"/>.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="typedElement">The FHIR resource as an <see cref="ITypedElement"/>.</param>
    /// <returns>The evaluation result containing rows.</returns>
    ViewDefinitionResult Evaluate(string viewDefinitionJson, ITypedElement typedElement);

    /// <summary>
    /// Evaluates a ViewDefinition (as JSON) against multiple FHIR resources.
    /// </summary>
    /// <param name="viewDefinitionJson">The ViewDefinition JSON string.</param>
    /// <param name="resources">The FHIR resources to evaluate against.</param>
    /// <returns>The evaluation result containing rows from all resources.</returns>
    ViewDefinitionResult EvaluateMany(string viewDefinitionJson, IEnumerable<ResourceElement> resources);
}

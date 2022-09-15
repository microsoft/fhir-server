// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// Base abstract class for shared logic for FHIRPath Patch operations
    /// </summary>
    internal abstract class OperationBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OperationBase"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource for this operation.</param>
        /// <param name="po">Pending operation object for the patch request.</param>
        /// <param name="provider">Provider for model information needed for resource manipulation.</param>
        internal OperationBase(Resource resource, PendingOperation po, IStructureDefinitionSummaryProvider provider = null)
        {
            ResourceElement = resource.ToElementNode();
            Operation = po;
            Provider = provider ?? ModelInfoProvider.Instance.StructureDefinitionSummaryProvider;
        }

        /// <summary>
        /// Gets the element node representation of the patch operation target resource.
        /// </summary>
        internal ElementNode ResourceElement { get; }

        /// <summary>
        /// Gets the operation object representing the patch request.
        /// </summary>
        internal PendingOperation Operation { get; }

        /// <summary>
        /// Pointer to the FHIR definition provider in the ModelInfoProvider static class
        /// </summary>
        internal IStructureDefinitionSummaryProvider Provider { get; }

        /// <summary>
        /// Target node to apply the patch operation.
        /// </summary>
        internal ElementNode Target { get; set; }

        /// <summary>
        /// Gets the value of the patch operation value parameter as an ElementNode.
        /// </summary>
        internal virtual ElementNode ValueElementNode
        {
            get
            {
                var mapping = Target.Definition as PropertyMapping;
                if (mapping is PropertyMapping propMapping)
                {
                    return Operation.Value.GetElementNodeFromPart(propMapping);
                }

                throw new InvalidOperationException("Patch target must have a property mapping");
            }
        }

        /// <summary>
        /// All inheriting classes must implement an operation exeuction method.
        /// </summary>
        /// <returns>FHIR Resource as POCO.</returns>
        internal abstract Resource Execute();
    }
}

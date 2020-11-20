// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression for search performed for a compartment.
    /// </summary>
    /// <remarks>
    /// Compartment is predefined set of resources to extract for specific object.
    /// For example (not part of standard) we can have compartment for Patient in which would include:
    /// Claim (patient or payee) and Observation (subject only).
    /// By searching compartment for patient we need to return all Claims where Claim.patient or Clain.payee is that patient
    /// and all Observations where Observation.subject is same patient.
    /// </remarks>
    public class CompartmentSearchExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompartmentSearchExpression"/> class.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="compartmentId">The compartment id.</param>
        public CompartmentSearchExpression(string compartmentType, string compartmentId)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownCompartmentType(compartmentType), nameof(compartmentType));
            EnsureArg.IsNotNullOrWhiteSpace(compartmentId, nameof(compartmentId));

            CompartmentType = compartmentType;
            CompartmentId = compartmentId;
        }

        /// <summary>
        /// The compartment type.
        /// </summary>
        public string CompartmentType { get; }

        /// <summary>
        /// The compartment id.
        /// </summary>
        public string CompartmentId { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitCompartment(this, context);
        }

        public override string ToString()
        {
            return $"(Compartment {CompartmentType} '{CompartmentId}')";
        }
    }
}

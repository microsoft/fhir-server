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
    public class CompartmentSearchExpression : Expression
    {
        public CompartmentSearchExpression(string compartmentType, string compartmentId, bool includeOriginResource = false)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownCompartmentType(compartmentType), nameof(compartmentType));
            EnsureArg.IsNotNullOrWhiteSpace(compartmentId, nameof(compartmentId));

            CompartmentType = compartmentType;
            CompartmentId = compartmentId;
            IncludeOriginResource = includeOriginResource;
        }

        /// <summary>
        /// The compartment type.
        /// </summary>
        public string CompartmentType { get; }

        /// <summary>
        /// The compartment id.
        /// </summary>
        public string CompartmentId { get; }

        /// <summary>
        /// When true, the origin resource should be returned with the compartment resources. The default is false.
        /// </summary>
        public bool IncludeOriginResource { get; }

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

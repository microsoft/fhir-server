// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression for search performed for a compartment.
    /// </summary>
    public class CompartmentSearchExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompartmentSearchExpression"/> class.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="compartmentId">The compartment id.</param>
        /// <param name="smartUserCompartment">True is this compartment expression is being added due to smart restrictions</param>
        /// <param name="filteredResourceTypes">Resource types to filter</param>
        public CompartmentSearchExpression(string compartmentType, string compartmentId, bool smartUserCompartment = false, params string[] filteredResourceTypes)
        {
            EnsureArg.IsTrue(ModelInfoProvider.IsKnownCompartmentType(compartmentType), nameof(compartmentType));
            EnsureArg.IsNotNullOrWhiteSpace(compartmentId, nameof(compartmentId));

            CompartmentType = compartmentType;
            CompartmentId = compartmentId;
            FilteredResourceTypes = filteredResourceTypes ?? Array.Empty<string>();
            SmartUserCompartment = smartUserCompartment;
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
        /// Indicates if this compartment expression was added as a smart filter
        /// </summary>
        public bool SmartUserCompartment { get; }

        /// <summary>
        /// Resource types to filter for compartment search
        /// </summary>
        public IReadOnlyCollection<string> FilteredResourceTypes { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitCompartment(this, context);
        }

        public override string ToString()
        {
            return $"(Compartment {CompartmentType} '{CompartmentId}')";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(CompartmentSearchExpression));
            hashCode.Add(CompartmentType);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is CompartmentSearchExpression compartmentSearch && compartmentSearch.CompartmentType == CompartmentType;
        }
    }
}

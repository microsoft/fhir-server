// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an include expression (where an additional resource is included based on a reference)
    /// </summary>
    public class IncludeExpression : Expression
    {
        private IReadOnlyCollection<string> _requires;
        private IReadOnlyCollection<string> _produces;

        /// <summary>
        /// Initializes a new instance of the <see cref="IncludeExpression"/> class.
        /// </summary>
        /// <param name="resourceTypes">The resource that supports the reference.</param>
        /// <param name="referenceSearchParameter">THe search parameter that establishes the reference relationship.</param>
        /// <param name="sourceResourceType">The source type of the reference.</param>
        /// <param name="targetResourceType">The target type of the reference.</param>
        /// <param name="referencedTypes">All the resource types referenced by resourceType</param>
        /// <param name="wildCard">If this is a wildcard reference include (include all referenced resources).</param>
        /// <param name="reversed">If this is a reversed include (revinclude) expression.</param>
        /// <param name="iterate"> If :iterate (:recurse) modifer was applied.</param>
        public IncludeExpression(string[] resourceTypes, SearchParameterInfo referenceSearchParameter, string sourceResourceType, string targetResourceType, IEnumerable<string> referencedTypes, bool wildCard, bool reversed, bool iterate)
        {
            EnsureArg.HasItems(resourceTypes, nameof(resourceTypes));

            if (!wildCard)
            {
                EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            }

            if (reversed)
            {
                EnsureArg.IsNotNull(sourceResourceType, nameof(sourceResourceType));
            }

            ResourceTypes = resourceTypes;
            ReferenceSearchParameter = referenceSearchParameter;
            SourceResourceType = sourceResourceType;
            TargetResourceType = targetResourceType;
            ReferencedTypes = referencedTypes?.ToList();
            WildCard = wildCard;
            Reversed = reversed;
            Iterate = iterate;
            CircularReference = TargetResourceType != null ? SourceResourceType == TargetResourceType
                                                           : ReferenceSearchParameter?.TargetResourceTypes != null && ReferenceSearchParameter.TargetResourceTypes.Contains(sourceResourceType);
        }

        /// <summary>
        /// Gets the resource type which is being searched.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Array property")]
        public string[] ResourceTypes { get; }

        /// <summary>
        /// Gets the reference search parameter for the relationship.
        /// </summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>
        /// Gets the source resource type. Value will be null if none are specified.
        /// </summary>
        public string SourceResourceType { get; }

        /// <summary>
        /// Gets the target resource type. Value will be null if none are specified.
        /// </summary>
        public string TargetResourceType { get; }

        /// <summary>
        ///  Gets the type of resources referenced by resourceType. Used when iterating over wildcard results.
        /// </summary>
        public IReadOnlyCollection<string> ReferencedTypes { get; }

        /// <summary>
        ///  Gets the type of resources the expression requires (includes from).
        /// </summary>
        public IReadOnlyCollection<string> Requires => _requires ??= GetRequiredResources();

        /// <summary>
        ///  Gets the type of resources the expression produces.
        /// </summary>
        public IReadOnlyCollection<string> Produces => _produces ??= GetProducedResources();

        /// <summary>
        /// Gets if the expression is a wildcard include.
        /// </summary>
        public bool WildCard { get; }

        /// <summary>
        /// Get if the expression is reversed.
        /// </summary>
        public bool Reversed { get; }

        /// <summary>
        /// Gets if the expression has :iterate (:recurse) modifier.
        /// </summary>
        public bool Iterate { get; }

        /// <summary>
        /// Gets if the expression has a circular reference (source type = target type).
        /// </summary>
        public bool CircularReference { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitInclude(this, context);
        }

        public override string ToString()
        {
            var targetType = TargetResourceType != null ? $":{TargetResourceType}" : string.Empty;
            var iterate = Iterate ? " Iterate" : string.Empty;
            var reversed = Reversed ? "Reversed " : string.Empty;
            var wildcard = WildCard ? " Wildcard" : string.Empty;
            var paramName = ReferenceSearchParameter != null ? $" {ReferenceSearchParameter.Code}" : string.Empty;
            return $"({reversed}Include{iterate}{wildcard}{paramName}{targetType})";
        }

        private IReadOnlyCollection<string> GetRequiredResources()
        {
            if (Reversed)
            {
                if (TargetResourceType != null)
                {
                    return new List<string> { TargetResourceType };
                }
                else if (ReferenceSearchParameter?.TargetResourceTypes != null && ReferenceSearchParameter.TargetResourceTypes.Any())
                {
                    return ReferenceSearchParameter.TargetResourceTypes;
                }
                else if (WildCard)
                {
                    return ReferencedTypes;
                }

                // impossible case
                return new List<string>();
            }
            else
            {
                return new List<string> { SourceResourceType };
            }
        }

        private IReadOnlyCollection<string> GetProducedResources()
        {
            if (Reversed)
            {
                return new List<string> { SourceResourceType };
            }
            else
            {
                if (TargetResourceType != null)
                {
                    return new List<string> { TargetResourceType };
                }
                else if (ReferenceSearchParameter?.TargetResourceTypes != null && ReferenceSearchParameter.TargetResourceTypes.Any())
                {
                    return ReferenceSearchParameter.TargetResourceTypes;
                }
                else if (WildCard)
                {
                    return ReferencedTypes;
                }

                // impossible case
                return new List<string>();
            }
        }
    }
}

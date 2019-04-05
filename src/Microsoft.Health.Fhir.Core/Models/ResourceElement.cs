// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Wraps an ITypedElement that contains FHIR data (generic to a specific version of FHIR)
    /// </summary>
    public class ResourceElement
    {
        private readonly Lazy<EvaluationContext> _context;

        public ResourceElement(ITypedElement instance)
        {
            EnsureArg.IsNotNull(instance, nameof(instance));

            Instance = instance;
            _context = new Lazy<EvaluationContext>(() => new EvaluationContext(instance));
        }

        public string InstanceType => Instance.InstanceType;

        public ITypedElement Instance { get; }

        public string Id => Scalar<string>("Resource.id");

        public string VersionId => Scalar<string>("Resource.meta.versionId");

        public DateTimeOffset? LastUpdated
        {
            get
            {
                // TODO: Find a better way to convert this (i.e. Resource.meta.lastUpdated.as(System.DateTime))
                var dateTime = Instance.Scalar("Resource.meta.lastUpdated", _context.Value);
                return DateTimeOffset.TryParse(dateTime?.ToString(), out var parsedDate) ? parsedDate : (DateTimeOffset?)null;
            }
        }

        public T Scalar<T>(string fhirPath)
        {
            object scalar = Instance.Scalar(fhirPath, _context.Value);

            return (T)scalar;
        }
    }
}

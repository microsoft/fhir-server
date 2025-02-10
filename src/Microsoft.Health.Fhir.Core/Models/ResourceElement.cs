﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Wraps an ITypedElement that contains generic FHIR data
    /// </summary>
    public class ResourceElement : IResourceElement
    {
        private readonly Lazy<EvaluationContext> _context;
        private readonly List<string> _nonDomainTypes = new List<string>
            {
                "Bundle",
                "Parameters",
                "Binary",
            };

        public ResourceElement(ITypedElement instance)
        {
            EnsureArg.IsNotNull(instance, nameof(instance));

            Instance = instance;
            _context = new Lazy<EvaluationContext>(() =>
                new EvaluationContext().WithResourceOverrides(instance));
        }

        internal ResourceElement(ITypedElement instance, object resourceInstance)
            : this(instance)
        {
            EnsureArg.IsNotNull(resourceInstance, nameof(resourceInstance));
            ResourceInstance = resourceInstance;
        }

        public string InstanceType => Instance.InstanceType;

        internal object ResourceInstance { get; }

        public ITypedElement Instance { get; }

        public string Id => Scalar<string>("Resource.id");

        public string VersionId => Scalar<string>("Resource.meta.versionId");

        public bool IsDomainResource => !_nonDomainTypes.Contains(InstanceType, StringComparer.OrdinalIgnoreCase);

        public DateTimeOffset? LastUpdated
        {
            get
            {
                var obj = Instance.Scalar("Resource.meta.lastUpdated", _context.Value);
                if (obj != null)
                {
                    return PrimitiveTypeConverter.ConvertTo<DateTimeOffset>(obj.ToString());
                }

                return null;
            }
        }

        public T Scalar<T>(string fhirPath)
        {
            object scalar = Instance.Scalar(fhirPath, _context.Value);
            return (T)scalar;
        }

        public IEnumerable<ITypedElement> Select(string fhirPath)
        {
            return Instance.Select(fhirPath, _context.Value);
        }

        public bool Predicate(string fhirPath)
        {
            return Instance.Predicate(fhirPath, _context.Value);
        }
    }
}

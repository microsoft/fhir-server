// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Introspection;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa.Features.Persistence
{
    /// <summary>
    /// Writes native Ignixa carriers without converting them to Firely resources.
    /// </summary>
    public sealed class IgnixaRawResourceFactory : IRawResourceFactory
    {
        private readonly IRawResourceFactory _nonNativeFactory;
        private readonly IgnixaSchemaContext _schemaContext;
        private readonly ModelInspector _inspector;

        /// <summary>
        /// Initializes a writer that delegates only carriers not owned by Ignixa.
        /// </summary>
        public IgnixaRawResourceFactory(IRawResourceFactory nonNativeFactory, IgnixaSchemaContext schemaContext, IModelInfoProvider modelInfoProvider)
        {
            ArgumentNullException.ThrowIfNull(nonNativeFactory);
            ArgumentNullException.ThrowIfNull(schemaContext);
            ArgumentNullException.ThrowIfNull(modelInfoProvider);
            _nonNativeFactory = nonNativeFactory;
            _schemaContext = schemaContext;
            Type patient = modelInfoProvider.GetTypeForFhirType("Patient")
                ?? throw new InvalidOperationException("The Firely model does not define Patient.");
            _inspector = ModelInspector.ForAssembly(patient.Assembly);
        }

        /// <inheritdoc />
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            ArgumentNullException.ThrowIfNull(resource);
            if (resource.ResourceInstance is not ResourceJsonNode native)
            {
                return _nonNativeFactory.Create(resource, keepMeta, keepVersion);
            }

            string data = FirelyCompatibleResourceJsonWriter.Write(native, _schemaContext.Schema, _inspector, keepMeta && !keepVersion ? "1" : null, !keepMeta);
            return new RawResource(data, FhirResourceFormat.Json, keepMeta);
        }
    }
}

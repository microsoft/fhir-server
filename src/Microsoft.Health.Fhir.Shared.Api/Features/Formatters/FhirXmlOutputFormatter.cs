// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class FhirXmlOutputFormatter : TextOutputFormatter
    {
        private readonly FhirXmlSerializer _fhirXmlSerializer;
        private readonly ResourceDeserializer _deserializer;
        private readonly IModelInfoProvider _modelInfoProvider;

        public FhirXmlOutputFormatter(FhirXmlSerializer fhirXmlSerializer, ResourceDeserializer deserializer, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(fhirXmlSerializer, nameof(fhirXmlSerializer));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _fhirXmlSerializer = fhirXmlSerializer;
            _deserializer = deserializer;
            _modelInfoProvider = modelInfoProvider;

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);

            SupportedMediaTypes.Add(KnownContentTypes.XmlContentType);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.TextXml);
            SupportedMediaTypes.Add(KnownMediaTypeHeaderValues.ApplicationAnyXmlSyntax);
        }

        protected override bool CanWriteType(Type type)
        {
            EnsureArg.IsNotNull(type, nameof(type));

            return typeof(Resource).IsAssignableFrom(type) || typeof(RawResourceElement).IsAssignableFrom(type);
        }

        public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(selectedEncoding, nameof(selectedEncoding));

            context.HttpContext.AllowSynchronousIO();

            var elementsSearchParameter = context.HttpContext.GetElementsOrDefault();
            var hasElements = elementsSearchParameter?.Any() == true;
            var summaryProvider = _modelInfoProvider.StructureDefinitionSummaryProvider;
            var additionalElements = new HashSet<string>();

            Resource resourceObject = null;
            if (typeof(RawResourceElement).IsAssignableFrom(context.ObjectType))
            {
                resourceObject = _deserializer.Deserialize(context.Object as RawResourceElement).ToPoco<Resource>();
                if (hasElements)
                {
                    var typeinfo = summaryProvider.Provide(resourceObject.TypeName);
                    var required = typeinfo.GetElements().Where(e => e.IsRequired).ToList();
                    additionalElements.UnionWith(required.Select(x => x.ElementName));
                }
            }
            else if (typeof(Hl7.Fhir.Model.Bundle).IsAssignableFrom(context.ObjectType))
            {
                // Need to set Resource property for resources in entries
                var bundle = context.Object as Hl7.Fhir.Model.Bundle;

                foreach (var entry in bundle.Entry.Where(x => x is RawBundleEntryComponent))
                {
                    var rawResource = entry as RawBundleEntryComponent;
                    entry.Resource = _deserializer.Deserialize(rawResource.ResourceElement).ToPoco<Resource>();
                    if (hasElements)
                    {
                        var typeinfo = summaryProvider.Provide(entry.Resource.TypeName);
                        var required = typeinfo.GetElements().Where(e => e.IsRequired).ToList();
                        additionalElements.UnionWith(required.Select(x => x.ElementName));
                    }
                }

                resourceObject = bundle;
            }
            else
            {
                resourceObject = (Resource)context.Object;
                if (hasElements)
                {
                    var typeinfo = summaryProvider.Provide(resourceObject.TypeName);
                    var required = typeinfo.GetElements().Where(e => e.IsRequired).ToList();
                    additionalElements.UnionWith(required.Select(x => x.ElementName));
                }
            }

            if (hasElements)
            {
                additionalElements.UnionWith(elementsSearchParameter);
                additionalElements.Add("meta");
            }

            HttpResponse response = context.HttpContext.Response;
            using (TextWriter textWriter = context.WriterFactory(response.Body, selectedEncoding))
            using (var writer = new XmlTextWriter(textWriter))
            {
                if (context.HttpContext.GetPrettyOrDefault())
                {
                    writer.Formatting = Formatting.Indented;
                }

                // I'll be happy to call async method here, but it crashes internally on call to XmlReader which doesn't implement
                // async version of certain methods.
#pragma warning disable CA1849 // Call async methods when in an async method
                _fhirXmlSerializer.Serialize(resourceObject, writer, context.HttpContext.GetSummaryTypeOrDefault(), elements: hasElements ? additionalElements.ToArray() : null);
#pragma warning restore CA1849 // Call async methods when in an async method
            }

            return Task.CompletedTask;
        }
    }
}

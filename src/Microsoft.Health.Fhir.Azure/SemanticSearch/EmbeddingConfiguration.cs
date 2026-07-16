// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Azure.SemanticSearch
{
    /// <summary>
    /// Configuration for the external embedding endpoint the FHIR server calls to turn text into vectors.
    /// Values are supplied through configuration; no secrets are stored here.
    /// </summary>
    public class EmbeddingConfiguration
    {
        /// <summary>
        /// Gets or sets the endpoint of the Azure OpenAI / Foundry resource,
        /// for example <c>https://my-resource.cognitiveservices.azure.com/</c>.
        /// </summary>
        public Uri Endpoint { get; set; }

        /// <summary>
        /// Gets or sets the name of the embedding model deployment, for example <c>text-embedding-3-small</c>.
        /// </summary>
        public string DeploymentName { get; set; }

        /// <summary>
        /// Gets or sets the number of dimensions to request from the model. Must match the vector column width.
        /// </summary>
        public int Dimensions { get; set; } = 1536;
    }
}

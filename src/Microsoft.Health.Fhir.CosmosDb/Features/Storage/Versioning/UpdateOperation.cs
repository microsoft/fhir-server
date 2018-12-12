// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////using EnsureThat;
////using Microsoft.Azure.Documents;

////namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
////{
////    /// <summary>
////    /// To be replaced by the BulkExecutor API:
////    /// https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.cosmosdb.bulkexecutor.bulkupdate.setupdateoperation-1?view=azure-dotnet
////    /// </summary>
////    internal class UpdateOperation : IUpdateOperation
////    {
////        public UpdateOperation(string property, object value)
////        {
////            EnsureArg.IsNotNullOrEmpty(property, nameof(property));

////            // value must not be null, if setting it to null is required, this should be an "unset" operation
////            // in the bulk execution API.
////            EnsureArg.IsNotNull(value, nameof(value));

////            Field = property;
////            Value = value;
////        }

////        public string Field { get; set; }

////        public object Value { get; set; }

////        public void Apply(Document resource)
////        {
////            EnsureArg.IsNotNull(resource, nameof(resource));

////            resource.SetPropertyValue(Field, Value);
////        }
////    }
////}

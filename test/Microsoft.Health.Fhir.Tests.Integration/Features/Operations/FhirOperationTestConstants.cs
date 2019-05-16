// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    public static class FhirOperationTestConstants
    {
        /// <summary>
        ///  The FHIR operations test require all job records to be deleted before each test is executed
        ///  so they can't run in parallel. Use this constant as the collection name so that tests within
        ///  this collection will run serially.
        /// </summary>
        public const string FhirOperationTests = "FHIR Operation Tests";
    }
}

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class Categories
    {
        public const string AnonymizedExport = nameof(AnonymizedExport);

        public const string AssemblyValidation = nameof(AssemblyValidation);

        public const string Audit = nameof(Audit);

        public const string Authorization = nameof(Authorization);

        public const string Bundle = nameof(Bundle);

        public const string BundleOrchestrator = nameof(BundleOrchestrator);

        public const string CompartmentSearch = nameof(CompartmentSearch);

        public const string ConditionalOperations = nameof(ConditionalOperations);

        /// <summary>
        /// Set of tests validating FHIR domain related logic.
        /// </summary>
        public const string Conformance = nameof(Conformance);

        public const string ConvertData = nameof(ConvertData);

        public const string Cors = nameof(Cors);

        public const string Crucible = nameof(Crucible);

        public const string CustomConvertData = nameof(CustomConvertData);

        public const string CustomHeaders = nameof(CustomHeaders);

        public const string CustomSearch = nameof(CustomSearch);

        public const string DataConversion = nameof(DataConversion);

        /// <summary>
        /// Set of tests validating data source functionalities (like, SQL, Cosmos, Storage Accounts).
        /// </summary>
        public const string DataSourceValidation = nameof(DataSourceValidation);

        public const string DomainLogicValidation = nameof(DomainLogicValidation);

        public const string Export = nameof(Export);

        public const string ExportDataValidation = nameof(ExportDataValidation);

        public const string ExportLongRunning = nameof(ExportLongRunning);

        public const string History = nameof(History);

        public const string Import = nameof(Import);

        public const string IndexAndReindex = nameof(IndexAndReindex);

        public const string MemberMatch = nameof(MemberMatch);

        public const string Operations = nameof(Operations);

        public const string Patch = nameof(Patch);

        public const string PatientEverything = nameof(PatientEverything);

        public const string Schema = nameof(Schema);

        public const string Search = nameof(Search);

        public const string Security = nameof(Security);

        public const string SearchParameterStatus = nameof(SearchParameterStatus);

        /// <summary>
        /// Set of tests validating serialization and deserialization logic.
        /// </summary>
        public const string Serialization = nameof(Serialization);

        public const string SmartOnFhir = nameof(SmartOnFhir);

        public const string Sort = nameof(Sort);

        public const string Transaction = nameof(Transaction);

        public const string Throttling = nameof(Throttling);

        public const string Validate = nameof(Validate);

        public const string Web = nameof(Web);

        public const string Xml = nameof(Xml);
    }
}

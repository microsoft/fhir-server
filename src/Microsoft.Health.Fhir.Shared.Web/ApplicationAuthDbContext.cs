// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;

namespace Microsoft.Health.Fhir.Web;

internal class ApplicationAuthDbContext(DbContextOptions<ApplicationAuthDbContext> options) : DbContext(options);

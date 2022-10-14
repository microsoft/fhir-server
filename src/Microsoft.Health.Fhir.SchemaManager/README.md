# Testing Fhir Schema Manager

[Main Fhir Schema Manager Documentation](https://github.com/microsoft/fhir-server/blob/main/docs/schema-manager.md)

---

### Requirements
1. [SQL Server Management Studio](https://docs.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms)
2. [Visual Studio 2022](https://visualstudio.microsoft.com/downloads)
3. Local SQL Server install
   - [SQL Server Developer or Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)

---

### Setup
1. Create a new local database (preferably named FHIR) using SSMS or Azure Data Studio.
2. [Clone the FHIR repo](https://github.com/microsoft/fhir-server.git).
3. Open **Microsoft.Health.Fhir.sln** in Visual Studio.

---

### Testing in Visual Studio

1. Set the **Microsoft.Health.Fhir.SchemaManager.Console** project as the Startup project.
2. Right-click the **Microsoft.Health.Fhir.SchemaManager.Console** project and select **Properties**.
3. On the left-hand side, click **Debug**, then click **Open debug launch profiles UI**, and then paste the following line into the top box (Command line arguments), overwriting anything there:
  `apply --connection-string "server=(local);Initial Catalog=FHIR;TrustServerCertificate=True;Integrated Security=True" --version 35`
4. Set a breakpoint in [ApplyCommand.cs (Line 54)](https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.SchemaManager/ApplyCommand.cs#L54).
5. Hit F5 to start debugging.

---

### Debugging healthcare-shared-components within fhir-server

The majority of the FHIR Schema Manager logic comes from healthcare-shared-components. You may notice while debugging that you skip some steps . Here's how to debug external code through the fhir-server solution in Visual Studio.

NOTE: There is currently a bug where you cannot authenticate with microsofthealthoss.visualstudio.com unless you have a Microsoft employee account.

1. [Uncheck "Enable Just My Code"](https://docs.microsoft.com/en-us/visualstudio/debugger/just-my-code).
2. Add the Microsoft OSS symbol server.
   - Symbol server to add: `microsofthealthoss.visualstudio.com`
   - [How to add symbol servers](https://docs.microsoft.com/en-us/visualstudio/debugger/specify-symbol-dot-pdb-and-source-files-in-the-visual-studio-debugger)
   - You may need to open `microsofthealthoss.visualstudio.com` in a web browser to properly authenticate.
3. Close and reopen Visual Studio.

To verify the steps above:
1. Set a breakpoint where FHIR Schema Manager calls ApplySchema from healthcare-shared-components:
    - [Microsoft.Health.Fhir.SchemaManager/ApplyCommand.cs](https://github.com/microsoft/fhir-server/blob/main/src/Microsoft.Health.Fhir.SchemaManager/ApplyCommand.cs#L54)
2. Start debugging by pressing **F5**.
3. When you hit the breakpoint, press **F11**.
4. Verify you've stepped into the ApplySchema function in healthcare-shared-components:
    - [Microsoft.Health.SqlServer/Features/Schema/Manager/SqlSchemaManager.cs](https://github.com/microsoft/healthcare-shared-components/blob/main/src/Microsoft.Health.SqlServer/Features/Schema/Manager/SqlSchemaManager.cs#L53)

# Schema Migration Guide
As Microsoft continues to develop the FHIR Server for Azure and support the newer releases of FHIR, migrations for the FHIR Server for Azure SQL Server edition will be required. The migration is a T-SQL script which alters the database. The script filename is encoded with the version where the version is an incrementing integer.

This document describes how to perform SQL Server schema migration on the FHIR Server for Azure application server.

 The database schemas will evolve with each release of new features and editions of FHIR. The corresponding FHIR Server for Azure would need to be upgraded to the next/latest version of the schema, which may require migration of the SQL Server schema.


## FHIR Schema Manager
Please refer to [Schema Manager Guide](https://github.com/microsoft/fhir-server/blob/main/docs/schema-manager.md) which is a command line app that upgrades the schema in SQL database from one version to the next through migration scripts.

## Methods for upgrading
1. Automatic schema upgrade

2. Schema upgrade via SQL Server schema migration tool

## 1. Automatic schema upgrade
When using the automatic schema upgrade method, the SQL Server schema migration is performed automatically on the FHIR Server for Azure upgrade.
The scripts which upgrade the SQL Server schema could be part of any upgrade package for the web application and run automatically when the web application launches.

In case of any errors during upgrade, the web application will not start and the upgrade script transaction will not get committed.

- ### Prerequisites

    - Application should have admin access to the database.

- ### When might you choose this method?

    - Non-Production scenarios - The auto upgrade is a good option for dev/test environments. This will automatically apply any pending migrations when the application launches. Hence, the testing of schema upgrade becomes part of the test cycle for the application.

## 2. Upgrade via SQL Server schema migration tool
The SQL Server schema migration tool is a command-line utility which admins can run to perform SQL Server schema migrations on demand.

The SQL Server schema upgrade scripts would still be part of any upgrade package for the web application but the scripts will not run automatically on the FHIR Server for Azure upgrade. To perform the SQL Server schema upgrade, the schema admin would need to run the SQL Server schema migration tool.

 - ### Prerequisites

    - The SQL Server schema migration tool is installed and available to use.

 - ### When might you choose this method?
    - Production environment
 
        It is highly recommended to use SQL Server schema migration tool for the production environment to avoid downtime. Automatic SQL Server schema migrations can take a long time, often in proportion to the size of the database. For this reason, performing SQL Server schema migrations on startup may not be a good option.
 
    - Role-based security
 
        Only Database/Schema Admin can run this tool to upgrade the SQL Server schema. It allows for proper setup of roles and permissions for the web application. For example - the application is assigned the role as Database Reader/Writer only.

- ### SQL Server schema migration tool

    The detailed information about this tool is provided [here](SchemaMigrationTool.md).

## Best practices for schema upgrade on SQL Server
- ### Back up the data before executing.
    
    In case something goes wrong during the implementation, you can’t afford to lose data. Make sure there are backup resources and that they’ve been tested before you proceed.
    
- ### Stick to the strategy.

    Too many data managers make a plan and then abandon it when the process goes “too” smoothly or when things get out of hand. The migration process can be complicated, so prepare for that reality and then 
    stick to the plan.

- ### Test, test

    Test before executing the migration and test after the migration is completed.  

 ## How to toggle between the options
There is a configurable property in the launchSettings.json which can be set to true/false based on the desired behavior. By default, the property is considered to be false.

`SqlServer:SchemaOptions:AutomaticUpdatesEnabled": "true"`

* If the property sets to 'true', then the SQL Server schema would be upgraded automatically on the server startup.
* If the property sets to 'false', then the SQL Server schema would remain in the same state. Moreover, the schema would only be upgraded by running the schema migration tool as needed.

This configurable property should be considered for each web project

 Note: This guide contains generalized advice and may not take your specific environment or application into account.

 ## Frequently Asked Questions

 Q1. What is upgraded first, app service or database schema?

 A1. The FHIR Server for Azure app service is upgraded first. Because firstly it contains the scripts to migrate the SQL Server schema and secondly the code is considered to work for a number of SQL Server schema versions and will specify the min and max supported versions.

 Q2. What should we do in case of automatic SQL Server schema upgrade fails?

 A2. If an automatic SQL Server schema upgrade fails and the web application is unable to start then you can toggle the SQL Server schema auto upgrade flag as mentioned under the [section](#How-to-toggle-between-the-options) so that the upgrade scripts would not run and application would start on the current version.

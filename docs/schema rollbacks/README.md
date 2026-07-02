# SQL Schema Rollbacks
When a schema change is added a coresponding document needs to be added to this folder. This document should contain the following information:
- The schema version as the name of the file.
- Any problems the new schema would have running with older versions of the software.
  - Each problem should be listed with the version of the software that would have the problem and a description of the problem.
  - Only versions equal to or newer than the currently released version of the software should be considered.
  - Examples of changes that would cause problems include:
    - Removing a parameter from a stored procedure.
    - Adding a required parameter to a stored procedure.
    - Changing the data type of a parameter in a stored procedure.
    - Changing the name of a parameter in a stored procedure.
    - Removing a stored procedure.
    - Changing the return type of a stored procedure.
    - Removing a column from a table or view.
    - Removing a table or view.
- Any steps that need to be taken to roll back the schema change.
  - This should include any scripts that need to be run to roll back the schema change, as well as any manual steps that need to be taken.
  - If the schema change is only additions and does not cause any problems with older versions of the software, then this section can be left blank or marked as "No rollback steps needed".
- Any unusual changes or notes about this schema change that may be helpful for future reference.

DECLARE @Names TABLE (Name varchar(100) PRIMARY KEY)
INSERT INTO @Names SELECT name FROM sys.objects WHERE type = 'p' AND name LIKE '%[0-9]' AND name NOT LIKE '%ResourceChanges%'
DECLARE @Name varchar(100)
WHILE EXISTS (SELECT * FROM @Names)
BEGIN
  SET @Name = (SELECT TOP 1 Name FROM @Names)
  EXECUTE('DROP PROCEDURE dbo.'+@Name)
  DELETE FROM @Names WHERE Name = @Name
END
GO
DECLARE @Names TABLE (Name varchar(100) PRIMARY KEY)
INSERT INTO @Names SELECT name FROM sys.types WHERE is_user_defined = 1 AND name LIKE '%[0-9]' AND name NOT IN ('SearchParamTableType_2','BulkReindexResourceTableType_1')
DECLARE @Name varchar(100)
WHILE EXISTS (SELECT * FROM @Names)
BEGIN
  SET @Name = (SELECT TOP 1 Name FROM @Names)
  EXECUTE('DROP TYPE dbo.'+@Name)
  DELETE FROM @Names WHERE Name = @Name
END
GO
IF EXISTS (SELECT * FROM sys.types WHERE name = 'CompartmentAssignmentList')
  DROP TYPE dbo.CompartmentAssignmentList
GO

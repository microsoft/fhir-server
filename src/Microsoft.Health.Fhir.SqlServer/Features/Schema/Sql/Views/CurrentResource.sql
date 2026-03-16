CREATE VIEW dbo.CurrentResource
AS
SELECT * 
  FROM dbo.Resource 
  WHERE IsHistory = 0
GO

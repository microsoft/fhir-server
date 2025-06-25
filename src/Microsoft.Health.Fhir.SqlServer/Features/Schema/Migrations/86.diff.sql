-- DROP TABLE EsportJob
GO
IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ExportJob]') AND type in (N'U'))
DROP TABLE [dbo].[ExportJob]
GO

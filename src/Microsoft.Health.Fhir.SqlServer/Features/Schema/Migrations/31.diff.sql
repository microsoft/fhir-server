/*************************************************************
    This migration adds new constraing on data length of rawresource
**************************************************************/

ALTER TABLE dbo.Resource WITH NOCHECK
    ADD CONSTRAINT RawResourceLength CHECK (DATALENGTH(RawResource) > 49)

GO

GO




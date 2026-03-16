IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2')
BEGIN    
    ALTER TABLE dbo.ReferenceTokenCompositeSearchParam Drop CONSTRAINT CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenDateTimeCompositeSearchParam Drop CONSTRAINT CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam Drop CONSTRAINT CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenQuantityCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam Drop CONSTRAINT CHK_TokenQuantityCompositeSearchParam_CodeOverflow1;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenStringCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenStringCompositeSearchParam Drop CONSTRAINT CHK_TokenStringCompositeSearchParam_CodeOverflow1;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenTokenCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenTokenCompositeSearchParam Drop CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow1;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenTokenCompositeSearchParam_CodeOverflow2')
BEGIN    
    ALTER TABLE dbo.TokenTokenCompositeSearchParam Drop CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow2;
END
GO

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenSearchParam_CodeOverflow')
BEGIN    
    ALTER TABLE dbo.TokenSearchParam Drop CONSTRAINT CHK_TokenSearchParam_CodeOverflow;
END

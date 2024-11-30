IF OBJECT_ID('dbo.ModelData') IS NULL begin
    print 'Creating table dbo.ModelData ...'

    CREATE TABLE dbo.ModelData(
        runQueueId uniqueidentifier NOT NULL,

        -- All the initial data that is needed to run the calculation.
        -- It is designed to be huge, and so zipped binary format is used.
        modelData varbinary(max) NOT NULL,

    CONSTRAINT PK_ModelData PRIMARY KEY CLUSTERED 
    (
	    runQueueId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.ModelData WITH CHECK ADD CONSTRAINT FK_ModelData_RunQueue FOREIGN KEY(runQueueId)
    REFERENCES dbo.RunQueue (runQueueId)
    ALTER TABLE dbo.ModelData CHECK CONSTRAINT FK_ModelData_RunQueue

end else begin
	print 'Table dbo.ModelData already exists ...'
end
go


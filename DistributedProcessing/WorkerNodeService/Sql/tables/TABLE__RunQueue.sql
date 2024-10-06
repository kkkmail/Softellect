IF OBJECT_ID('dbo.RunQueue') IS NULL begin
	print 'Creating table dbo.RunQueue ...'

	CREATE TABLE dbo.RunQueue(
        runQueueId uniqueidentifier not null,
        runQueueOrder bigint IDENTITY(1,1) not null,

        -- A solver id to determine which solver should run the model.
        -- This is needed because the modelData is stored in a zipped binary format.
        solverId uniqueidentifier not null,

        -- All the initial data that is needed to run the calculation.
        -- It is designed to be huge, and so zipped binary format is used.
        modelData varbinary(max) not null,

        runQueueStatusId int not null,
        processId int NULL,
        notificationTypeId int not null,
        errorMessage nvarchar(max) null,
        progress decimal(18, 14) not null,

        -- Additional progress data (if any) used for further analysis and / or for earlier termination.
        -- We want to store the progress data in JSON rather than zipped binary, so that to be able to write some queries when needed.
        progressData nvarchar(max) NULL,

        callCount bigint not null,
        evolutionTime decimal(18, 14) not null,

	        -- Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
        relativeInvariant float not null,

        createdOn datetime not null,
        startedOn datetime null,
        modifiedOn datetime not null,
        CONSTRAINT PK_WorkerNodeRunModelData PRIMARY KEY CLUSTERED 
        (
            runQueueId ASC
        ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR runQueueStatusId
    ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR notificationTypeId
    ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR progress
    ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR callCount
    ALTER TABLE dbo.RunQueue ADD DEFAULT ((0)) FOR evolutionTime
    ALTER TABLE dbo.RunQueue ADD DEFAULT ((1)) FOR relativeInvariant
    ALTER TABLE dbo.RunQueue ADD DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.RunQueue ADD DEFAULT (getdate()) FOR modifiedOn

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_NotificationType FOREIGN KEY(notificationTypeId)
    REFERENCES dbo.NotificationType (notificationTypeId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_NotificationType

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_RunQueueStatus FOREIGN KEY(runQueueStatusId)
    REFERENCES dbo.RunQueueStatus (runQueueStatusId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_RunQueueStatus

    ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_Solver

end else begin
	print 'Table dbo.RunQueue already exists ...'
end
go


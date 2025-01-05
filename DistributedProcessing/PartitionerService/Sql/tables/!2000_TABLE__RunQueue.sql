IF OBJECT_ID('dbo.RunQueue') IS NULL begin
    print 'Creating table dbo.RunQueue ...'

    CREATE TABLE dbo.RunQueue(
        runQueueId uniqueidentifier NOT NULL,
        runQueueOrder bigint IDENTITY(1,1) NOT NULL,

        -- A solver id to determine which solver should run the model.
        -- This is needed because the modelData is stored in a zipped binary format.
        solverId uniqueidentifier not null,

        runQueueStatusId int NOT NULL,
        processId int NULL,
        notificationTypeId int NOT NULL,
        errorMessage nvarchar(max) NULL,
        lastErrorOn datetime null,
        retryCount int not null,
        maxRetries int not null,
        progress decimal(38, 16) NOT NULL,

        -- Additional progress data (if any) used for further analysis and / or for earlier termination.
        -- We want to store the progress data in JSON rather than zipped binary, so that to be able to write some queries when needed.
        progressData nvarchar(max) NULL,

        callCount bigint NOT NULL,
        evolutionTime decimal(38, 16) not null,

	        -- Should be close to 1.0 all the time. Substantial deviations is a sign of errors. If not needed, then set to 1.0.
        relativeInvariant float NOT NULL,

        createdOn datetime NOT NULL,
        startedOn datetime NULL,
        modifiedOn datetime NOT NULL,

        -- Partitioner has extra column to account for the worker node running the calculation.
        workerNodeId uniqueidentifier NULL,

    CONSTRAINT PK_RunQueue PRIMARY KEY CLUSTERED 
    (
        runQueueId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_runQueueStatusId DEFAULT ((0)) FOR runQueueStatusId
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_notificationTypeId DEFAULT ((0)) FOR notificationTypeId
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_progress DEFAULT ((0)) FOR progress
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_callCount DEFAULT ((0)) FOR callCount
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_evolutionTime DEFAULT ((0)) FOR evolutionTime
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_relativeInvariant DEFAULT ((1)) FOR relativeInvariant
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_modifiedOn DEFAULT (getdate()) FOR modifiedOn
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_retryCount DEFAULT ((0)) FOR retryCount
    ALTER TABLE dbo.RunQueue ADD CONSTRAINT DF_RunQueue_maxRetries DEFAULT ((0)) FOR maxRetries

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_NotificationType FOREIGN KEY(notificationTypeId)
    REFERENCES dbo.NotificationType (notificationTypeId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_NotificationType

    ALTER TABLE dbo.RunQueue WITH CHECK ADD CONSTRAINT FK_RunQueue_RunQueueStatus FOREIGN KEY(runQueueStatusId)
    REFERENCES dbo.RunQueueStatus (runQueueStatusId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_RunQueueStatus

    ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_Solver

    ALTER TABLE dbo.RunQueue  WITH CHECK ADD  CONSTRAINT FK_RunQueue_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.RunQueue CHECK CONSTRAINT FK_RunQueue_WorkerNode

end else begin
    print 'Table dbo.RunQueue already exists ...'
end
go


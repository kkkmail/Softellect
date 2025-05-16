IF OBJECT_ID('dbo.NotificationType') IS NULL begin
    print 'Creating table dbo.NotificationType ...'

    CREATE TABLE dbo.NotificationType(
        notificationTypeId int NOT NULL,
        notificationTypeName nvarchar(50) NOT NULL,
    CONSTRAINT PK_NotificationType PRIMARY KEY CLUSTERED 
    (
        notificationTypeId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_NotificationType ON dbo.NotificationType
    (
        notificationTypeName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.NotificationType already exists ...'
end
go


IF OBJECT_ID('dbo.RunQueueStatus') IS NULL begin
    print 'Creating table dbo.RunQueueStatus ...'

    CREATE TABLE dbo.RunQueueStatus(
        runQueueStatusId int NOT NULL,
        runQueueStatusName nvarchar(50) NOT NULL,
    CONSTRAINT PK_RunQueueStatus PRIMARY KEY CLUSTERED 
    (
        runQueueStatusId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_RunQueueStatus ON dbo.RunQueueStatus
    (
        runQueueStatusName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.RunQueueStatus already exists ...'
end
go


IF OBJECT_ID('dbo.Solver') IS NULL begin
    print 'Creating table dbo.Solver ...'

    CREATE TABLE dbo.Solver(
        solverId uniqueidentifier not null,
        solverOrder bigint identity(1,1) not null,
        solverName nvarchar(100) not null,
        description nvarchar(2000) null, 
        solverData varbinary(max) null,
        solverHash char(64) not null,
        createdOn datetime not null,
        isInactive bit not null,
    CONSTRAINT PK_Solver PRIMARY KEY CLUSTERED 
    (
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.Solver ADD CONSTRAINT DF_Solver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.Solver ADD CONSTRAINT DF_Solver_isInactive DEFAULT (getdate()) FOR isInactive

    CREATE UNIQUE NONCLUSTERED INDEX UX_Solver_solverName ON dbo.Solver
    (
        solverName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]

end else begin
    print 'Table dbo.Solver already exists ...'
end
go

IF OBJECT_ID('dbo.WorkerNode') IS NULL begin
    print 'Creating table dbo.WorkerNode ...'

    CREATE TABLE dbo.WorkerNode(
        workerNodeId uniqueidentifier NOT NULL,
        workerNodeOrder bigint IDENTITY(1,1) NOT NULL,
        workerNodeName nvarchar(100) NOT NULL,
        nodePriority int NOT NULL,
        numberOfCores int NOT NULL,
        description nvarchar(1000) NULL,
        isInactive bit NOT NULL,
        workerNodePublicKey varbinary(max) NULL,
        createdOn datetime NOT NULL,
        modifiedOn datetime NOT NULL,
    CONSTRAINT PK_WorkerNode PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_isInactive  DEFAULT ((0)) FOR isInactive
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_nodePriority  DEFAULT ((100)) FOR nodePriority
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_numberOfCores  DEFAULT ((0)) FOR numberOfCores
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_createdOn  DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNode ADD  CONSTRAINT DF_WorkerNode_modifiedOn  DEFAULT (getdate()) FOR modifiedOn

    CREATE UNIQUE NONCLUSTERED INDEX UX_WorkerNodeName ON dbo.WorkerNode
    (
        workerNodeName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]

    CREATE UNIQUE NONCLUSTERED INDEX UX_WorkerNodeOrder ON dbo.WorkerNode
    (
        workerNodeOrder ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
    print 'Table dbo.WorkerNode already exists ...'
end
go

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

IF OBJECT_ID('dbo.Setting') IS NULL begin
    print 'Creating table dbo.Setting ...'

    CREATE TABLE dbo.Setting(
        settingName nvarchar(100) NOT NULL,
        settingBool bit NULL,
        settingGuid uniqueidentifier NULL,
        settingLong bigint NULL,
        settingText nvarchar(max) NULL,
        settingBinary varbinary(max) NULL,
        createdOn datetime not null,
    CONSTRAINT PK_Setting PRIMARY KEY CLUSTERED 
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.Setting ADD CONSTRAINT DF_Setting_createdOn DEFAULT (getdate()) FOR createdOn

    CREATE UNIQUE NONCLUSTERED INDEX UX_Setting ON dbo.Setting
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
	print 'Table dbo.Setting already exists ...'
end
go



IF OBJECT_ID('dbo.WorkerNodeSolver') IS NULL begin
    print 'Creating table dbo.WorkerNodeSolver ...'

    CREATE TABLE dbo.WorkerNodeSolver(
        workerNodeId uniqueidentifier not null,
        solverId uniqueidentifier not null,
        createdOn datetime not null,
        isDeployed bit not null,
        deploymentError nvarchar(max) null,
        CONSTRAINT PK_WorkerNodeSolver PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC,
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNodeSolver ADD CONSTRAINT DF_WorkerNodeSolver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNodeSolver ADD CONSTRAINT DF_WorkerNodeSolver_isDeployed DEFAULT (0) FOR isDeployed

    ALTER TABLE dbo.WorkerNodeSolver  WITH CHECK ADD  CONSTRAINT FK_WorkerNodeSolver_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.WorkerNodeSolver CHECK CONSTRAINT FK_WorkerNodeSolver_WorkerNode

    ALTER TABLE dbo.WorkerNodeSolver  WITH CHECK ADD  CONSTRAINT FK_WorkerNodeSolver_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.WorkerNodeSolver CHECK CONSTRAINT FK_WorkerNodeSolver_Solver

end else begin
    print 'Table dbo.WorkerNodeSolver already exists ...'
end
go


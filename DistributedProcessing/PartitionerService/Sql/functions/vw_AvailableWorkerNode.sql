drop view if exists vw_AvailableWorkerNode
go


create view vw_AvailableWorkerNode
as
with le as
(
select
    workerNodeId
    ,solverId
    ,max(lastErrorOn) as lastErrorOn
from RunQueue r
where workerNodeId is not null and lastErrorOn is not null
group by r.workerNodeId, r.solverId
)
,a as
(
select
    w.workerNodeId
    ,s.solverId
    ,nodePriority
    ,isnull(cast(
        case
            when numberOfCores <= 0 then 1
            else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
        end as money), 0) as workLoad
    ,case when le.lastErrorOn is null then null else datediff(minute, getdate(), le.lastErrorOn) end as lastErrMinAgo
from WorkerNode w
inner join WorkerNodeSolver s on w.workerNodeId = s.workerNodeId
left outer join le on s.workerNodeId = le.solverId and s.solverId = le.solverId
where w.isInactive = 0 and s.isDeployed = 1
)
select
    a.*, 
    c.new_id as OrderId
    from a
    cross apply (select new_id from vw_newid) c

go


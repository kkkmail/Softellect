@echo off
copy /b /y .\DistributedProcessing\Core\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\MessagingService\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\ModelGenerator\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\partitionerAdm\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\partitionerService\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\SolverRunner\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\DistributedProcessing\WorkerNodeService\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\Messaging\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\MessagingService\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\Analytics\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\Sys\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y .\Wcf\bin\x64\Release\*.nupkg  ..\Packages\
copy /b /y ..\OdePackInterop\OdePackInterop\bin\x64\Release\*.nupkg  ..\Packages\

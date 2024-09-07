md .\!All
del .\!All\all.sql
del .\!All\001_all.sql
del .\!All\002_data.sql

copy /b .\tables\*.sql + .\functions\*.sql + .\procedures\*.sql + ..\..\..\MessagingService\Sql\tables\*.sql + ..\..\..\MessagingService\Sql\procedures\*.sql + .\data\*.sql + ..\..\..\MessagingService\Sql\data\*.sql .\!All\all.sql

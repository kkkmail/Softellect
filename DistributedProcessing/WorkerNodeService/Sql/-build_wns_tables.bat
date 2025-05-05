md .\!All
del .\!All\all.sql
del .\!All\001_all.sql
del .\!All\002_data.sql

copy /b .\tables\*.sql .\!All\all_wns_tables.sql

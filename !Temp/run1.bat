pktmon stop
pktmon reset
pktmon filter remove
pktmon filter add -p 47291
pktmon start --etw -m real-time --file-name C:\GitHub\Softellect\!Temp\vpn.etl
# pktmon stop
# pktmon pcapng C:\GitHub\Softellect\!Temp\vpn.etl -o C:\GitHub\Softellect\!Temp\vpn.pcapng
# pktmon format C:\GitHub\Softellect\!Temp\vpn.etl -o C:\GitHub\Softellect\!Temp\vpn.txt

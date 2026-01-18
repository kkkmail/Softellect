pktmon stop
pktmon reset
pktmon filter remove

pktmon filter add -i 72.167.224.50
pktmon filter add -t UDP
pktmon filter add -p 45001

pktmon start --etw -m real-time --file-name C:\GitHub\Softellect\!Temp\vpn_windows.etl
# pktmon stop
# pktmon pcapng C:\GitHub\Softellect\!Temp\vpn_windows.etl -o C:\GitHub\Softellect\!Temp\vpn_windows.pcapng
# pktmon format C:\GitHub\Softellect\!Temp\vpn_windows.etl -o C:\GitHub\Softellect\!Temp\vpn_windows.txt

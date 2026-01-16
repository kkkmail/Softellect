pktmon stop
pktmon reset
pktmon filter remove

pktmon filter add -i 216.219.95.164
pktmon filter add -t UDP
pktmon filter add -p 47291
# pktmon filter add -p 45001

pktmon start --etw -m real-time --file-name C:\GitHub\Softellect\!Temp\vpn_linux.etl
# pktmon stop
# pktmon pcapng C:\GitHub\Softellect\!Temp\vpn_linux.etl -o C:\GitHub\Softellect\!Temp\vpn_linux.pcapng
# pktmon format C:\GitHub\Softellect\!Temp\vpn_linux.etl -o C:\GitHub\Softellect\!Temp\vpn_linux.txt

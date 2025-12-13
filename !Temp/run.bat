pktmon stop
pktmon reset
pktmon filter remove
pktmon filter add -p 53
pktmon start --etw -p 0 -c 1 --file-name C:\GitHub\Softellect\!Temp\vpn_dns.etl
nslookup -timeout=10 dns.msftncsi.com 10.66.77.1
pktmon stop
pktmon pcapng C:\GitHub\Softellect\!Temp\vpn_dns.etl -o C:\GitHub\Softellect\!Temp\vpn_dns.pcapng

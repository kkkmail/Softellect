pktmon filter remove
pktmon filter add -i <IFINDEX>
pktmon filter add -p 53
pktmon start --etw -p 0 --file-name c:\GitHub\Softellect\!Temp\vpn_dns.etl


nslookup -timeout=10 dns.msftncsi.com 10.66.77.1


pktmon stop
pktmon format c:\GitHub\Softellect\!Temp\vpn_dns.etl -o c:\GitHub\Softellect\!Temp\vpn_dns.pcapng

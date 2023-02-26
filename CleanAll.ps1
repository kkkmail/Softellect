cls

echo "Terminating known orphanged VB / Rider processes..."

$msb = Get-Process -Name "MSBuild" -ea silentlycontinue
if ($msb) { Stop-Process -InputObject $msb -force }
Get-Process | Where-Object {$_.HasExited}

$vbcsc = Get-Process -Name "VBCSCompiler" -ea silentlycontinue
if ($vbcsc) { Stop-Process -InputObject $vbcsc -force }
Get-Process | Where-Object {$_.HasExited}

$w3wp = Get-Process -Name "w3wp" -ea silentlycontinue
if ($w3wp) { Stop-Process -InputObject $w3wp -force }
Get-Process | Where-Object {$_.HasExited}

if ((!$msb) -and (!$vbcsc) -and (!$w3wp)) { echo "No known orphanded processes found!" }
                                          

echo "Deleting all bin and obj content..."

Remove-Item -path .\NugetPackages\*.nupkg -force -ea silentlycontinue

Remove-Item -path .\Data\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Interop\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Messaging\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\MessagingData\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Platform\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgClientOne\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgClientTwo\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgService\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgServiceinfo\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgWorker\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\NetCoreClient\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\NetCoreService\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfClient\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfService\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfServiceInfo\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfWorker\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfWorker2\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WebWorker\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\ServiceProxy\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Sys\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\TestUpdateJson\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\TestUpdateJson2\bin -recurse -force -ea silentlycontinue
Remove-Item -path .\Wcf\bin -recurse -force -ea silentlycontinue

Remove-Item -path .\Data\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Interop\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Messaging\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\MessagingData\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Platform\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgClientOne\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgClientTwo\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgService\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgServiceinfo\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Msg\MsgWorker\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\NetCoreClient\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\NetCoreService\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfClient\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfService\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfServiceInfo\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfWorker\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WcfWorker2\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Samples\Wcf\WebWorker\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\ServiceProxy\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Sys\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\TestUpdateJson\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\TestUpdateJson2\obj -recurse -force -ea silentlycontinue
Remove-Item -path .\Wcf\obj -recurse -force -ea silentlycontinue


echo "Deleting all garbage from user Temp folder..."
Remove-Item -path $env:userprofile\AppData\Local\Temp -recurse -force -ea silentlycontinue

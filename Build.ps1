# Build the WinForms application with the Windows inbox .NET Framework compiler and WinRT metadata.
$framework = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:windir 'System32\WinMetadata'
$references = @('Windows.Devices','Windows.Foundation','Windows.Storage') | ForEach-Object {
    "/r:$metadata\$_.winmd"
}
$references += @('System.Runtime.WindowsRuntime','System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Threading.Tasks','System.Collections','System.Runtime.InteropServices') | ForEach-Object {
    "/r:$framework\$_.dll"
}
& "$framework\csc.exe" /nologo /utf8output /target:winexe /main:HeartRateWindow /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$PSScriptRoot\HeartRate.exe" @references "$PSScriptRoot\HeartRateWindow.cs" "$PSScriptRoot\probe\HeartRateProbe.cs"
exit $LASTEXITCODE

$ErrorActionPreference = 'Continue'
Write-Output "== OS =="
Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture | Format-List

Write-Output "== CPU =="
Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors | Format-List

Write-Output "== RAM (GB) =="
[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)

Write-Output "== GPU =="
Get-CimInstance Win32_VideoController | Select-Object Name, AdapterRAM, DriverVersion | Format-List

Write-Output "== nvidia-smi =="
try { nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv } catch { "not available" }

Write-Output "== Disks =="
Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}, @{n='UsedGB';e={[math]::Round($_.Used/1GB,1)}} | Format-Table

Write-Output "== .NET =="
try { dotnet --list-sdks } catch { "dotnet SDK check failed" }
try { dotnet --list-runtimes } catch { "dotnet runtime check failed" }

Write-Output "== Ollama CLI =="
try { ollama --version; ollama list } catch { "ollama CLI not on PATH" }

Write-Output "== Ollama API =="
try { (Invoke-RestMethod http://127.0.0.1:11434/api/version).version } catch { "Ollama API not reachable on 127.0.0.1:11434" }

Write-Output "== Tools =="
foreach ($t in 'tesseract','soffice','git','pwsh','winget') {
    $src = (Get-Command $t -ErrorAction SilentlyContinue).Source
    "{0}: {1}" -f $t, $src
}

Write-Output "== User language list =="
try { Get-WinUserLanguageList | Select-Object LanguageTag } catch { "Unable to get user language list" }

Write-Output "== Windows OCR Recognizer Languages =="
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
    [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | Select-Object LanguageTag, DisplayName | Format-Table
} catch {
    "WinRT OCR query failed: $_"
}

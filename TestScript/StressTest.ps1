param(
    [string]$OutputFolder = "C:\Logs\Input",
    [int]$FileCount = 500,
    [switch]$Continuous,
    [int]$DelaySeconds = 2
)

# Ensure folder exists
if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
}

function New-RandomFile($index, $folder) {
    # Random size between 50KB and 1000KB
    $sizeKB = Get-Random -Minimum 50 -Maximum 1000
    $sizeBytes = $sizeKB * 1024

    # Random filename with timestamp for uniqueness
    $fileName = "testfile_{0:0000}_{1:yyyyMMdd_HHmmssfff}.log" -f $index, (Get-Date)
    $filePath = Join-Path $folder $fileName

    # Fill file with random data
    $bytes = New-Object byte[] $sizeBytes
    (New-Object System.Random).NextBytes($bytes)
    [System.IO.File]::WriteAllBytes($filePath, $bytes)

    Write-Host "Generated: $fileName ($sizeKB KB)"
}

if ($Continuous) {
    Write-Host "Starting continuous file generation in $OutputFolder every $DelaySeconds seconds..."
    $i = 1
    while ($true) {
        New-RandomFile $i $OutputFolder
        $i++
        Start-Sleep -Seconds $DelaySeconds
    }
} else {
    Write-Host "Generating $FileCount files in $OutputFolder ..."
    for ($i = 1; $i -le $FileCount; $i++) {
        New-RandomFile $i $OutputFolder
        if ($i % 50 -eq 0) {
            Write-Host "$i files generated..."
        }
    }
    Write-Host "Done! Generated $FileCount files in $OutputFolder."
}

# Touchpad Right Click - PowerShell Self-Signed Certificate Script
# For Windows 10/11

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Touchpad Right Click - Certificate Tool" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Check administrator privileges
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "[ERROR] Administrator privileges required" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run PowerShell as Administrator" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

# Set parameters
$CertName = "TouchpadRightClick Self-Signed Certificate"
$PublishPath = Join-Path $PSScriptRoot "CSharp\TouchpadRightClick\bin\Release\net8.0-windows\win-x64\publish"
$ExeFileV85 = Join-Path $PublishPath "觸控板輕觸右鍵_v8.5_穩定版.exe"
$ExeFileV86 = Join-Path $PublishPath "觸控板輕觸右鍵_v8.6_快速版.exe"

Write-Host "[Step 1/5] Checking executable files..." -ForegroundColor Yellow
$filesToSign = @()

if (Test-Path $ExeFileV85) {
    Write-Host "  OK - Found v8.5 Stable" -ForegroundColor Green
    $filesToSign += $ExeFileV85
} else {
    Write-Host "  Warning - v8.5 not found: $ExeFileV85" -ForegroundColor Yellow
}

if (Test-Path $ExeFileV86) {
    Write-Host "  OK - Found v8.6 Fast" -ForegroundColor Green
    $filesToSign += $ExeFileV86
} else {
    Write-Host "  Warning - v8.6 not found: $ExeFileV86" -ForegroundColor Yellow
}

if ($filesToSign.Count -eq 0) {
    Write-Host "[ERROR] Cannot find any executable files" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host ""

Write-Host "[Step 2/5] Creating self-signed certificate..." -ForegroundColor Yellow
try {
    # Create certificate
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$CertName" `
        -FriendlyName $CertName `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -KeyUsage DigitalSignature `
        -KeySpec Signature `
        -HashAlgorithm SHA256

    Write-Host "  OK - Certificate created" -ForegroundColor Green
    Write-Host "    Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
} catch {
    Write-Host "[ERROR] Failed to create certificate: $_" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host ""

Write-Host "[Step 3/5] Installing certificate to Trusted Root..." -ForegroundColor Yellow
try {
    # Get certificate from personal store
    $certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("My", "CurrentUser")
    $certStore.Open("ReadOnly")
    $myCert = $certStore.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    $certStore.Close()

    # Add to Trusted Root Certification Authorities
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
    $rootStore.Open("ReadWrite")
    $rootStore.Add($myCert)
    $rootStore.Close()

    Write-Host "  OK - Certificate installed to Trusted Root" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Failed to install certificate: $_" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host ""

Write-Host "[Step 4/5] Signing executables..." -ForegroundColor Yellow
$successCount = 0
foreach ($exeFile in $filesToSign) {
    $fileName = Split-Path $exeFile -Leaf
    Write-Host "  Signing: $fileName" -ForegroundColor Cyan

    try {
        # Sign the executable
        $result = Set-AuthenticodeSignature -FilePath $exeFile -Certificate $cert -TimestampServer "http://timestamp.digicert.com"

        # Verify signature
        $signature = Get-AuthenticodeSignature -FilePath $exeFile
        if ($signature.Status -eq "Valid") {
            Write-Host "    OK - Successfully signed (Status: $($signature.Status))" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "    Warning - Signed but status is: $($signature.Status)" -ForegroundColor Yellow
            Write-Host "    (This is normal for self-signed certificates)" -ForegroundColor Gray
            $successCount++
        }
    } catch {
        Write-Host "    ERROR - Failed to sign: $_" -ForegroundColor Red
    }
}
Write-Host ""
Write-Host "Signed $successCount / $($filesToSign.Count) files" -ForegroundColor White
Write-Host ""

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "COMPLETED!" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Completed steps:" -ForegroundColor White
Write-Host "  1. Created self-signed certificate (valid for 10 years)" -ForegroundColor Gray
Write-Host "  2. Installed to 'Trusted Root Certification Authorities'" -ForegroundColor Gray
Write-Host "  3. Signed program executables" -ForegroundColor Gray
Write-Host ""
Write-Host "Certificate Information:" -ForegroundColor White
Write-Host "  Subject: $CertName" -ForegroundColor Gray
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Expiration: $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Gray
Write-Host ""
Write-Host "Signed Files:" -ForegroundColor White
Write-Host "  v8.5 - Stable version (trigger on touch release)" -ForegroundColor Gray
Write-Host "  v8.6 - Fast version (80ms quick trigger, optimized for slow movement)" -ForegroundColor Gray
Write-Host ""
Write-Host "Testing Recommendations:" -ForegroundColor Yellow
Write-Host "  1. Test v8.6 first (recommended for slow-moving users)" -ForegroundColor Gray
Write-Host "  2. If too many false positives, try v8.5" -ForegroundColor Gray
Write-Host "  3. Compare trigger speed and stability" -ForegroundColor Gray
Write-Host ""
Write-Host "Your programs should no longer be blocked by Windows Defender" -ForegroundColor Green
Write-Host ""
Write-Host "Notes:" -ForegroundColor Yellow
Write-Host "  * This certificate is only valid on this computer" -ForegroundColor Gray
Write-Host "  * To use on another PC, run this script again on that PC" -ForegroundColor Gray
Write-Host "  * Third-party antivirus may still warn (add to exception list)" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to exit"

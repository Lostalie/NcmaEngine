# Trusted local verification input binding. No test/build/install on import/open.
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')

function Open-PackageVerificationContext([string]$ScriptRoot,[string]$VerificationWorkspace,
    [string]$CapturedBuildRoot,[string]$ExpectedCaptureManifestSha256) {
    $scriptDirectory=[IO.Path]::GetFullPath($ScriptRoot).TrimEnd('\')
    $explicit=[bool]$VerificationWorkspace -or [bool]$CapturedBuildRoot -or [bool]$ExpectedCaptureManifestSha256
    if(-not $explicit){
        return [pscustomobject]@{Workspace=(Split-Path -Parent $scriptDirectory);Capture=$null;SourceInputsSha256=$null}
    }
    if(-not $VerificationWorkspace -or -not $CapturedBuildRoot -or $ExpectedCaptureManifestSha256 -cnotmatch '^[0-9A-F]{64}$'){
        throw 'package_verification_binding_required'
    }
    foreach($path in @($VerificationWorkspace,$CapturedBuildRoot,$ScriptRoot)){
        if($path -notmatch '^[A-Za-z]:[\\/]'){throw 'package_verification_absolute_path'}
    }
    $workspace=[IO.Path]::GetFullPath($VerificationWorkspace).TrimEnd('\')
    $checkout=@(& git -C $workspace rev-parse --show-toplevel)
    if($LASTEXITCODE -ne 0 -or $checkout.Count -ne 1 -or
        [IO.Path]::GetFullPath([string]$checkout[0]).TrimEnd('\') -ine $workspace){throw 'package_verification_workspace'}
    $capture=$null
    try{
        $capture=Open-CapturedBuild $workspace $CapturedBuildRoot
        if($capture.ManifestSha256 -cne $ExpectedCaptureManifestSha256){throw 'package_verification_capture_changed'}
        if($scriptDirectory -ine (Join-Path $capture.Source 'scripts') -or -not(Test-Path -LiteralPath $scriptDirectory -PathType Container)){throw 'package_verification_script_root'}
        return [pscustomobject]@{Workspace=$workspace;Capture=$capture;SourceInputsSha256=$capture.Manifest.sourceInputsSha256}
    }catch{if($null -ne $capture){$capture.Pins.Dispose()};throw}
}

function Assert-PackageVerificationCapture($Context,$Value) {
    if($null -eq $Context.Capture){return}
    foreach($name in @('capturedBuildRoot','captureManifestSha256','sourceInputsSha256')){
        if($null -eq $Value -or $null -eq $Value.PSObject.Properties[$name]){throw 'package_verification_uncaptured_input'}
    }
    if($Value.capturedBuildRoot -cne $Context.Capture.Root -or
        $Value.captureManifestSha256 -cne $Context.Capture.ManifestSha256 -or
        $Value.sourceInputsSha256 -cne $Context.SourceInputsSha256){throw 'package_verification_input_capture_mismatch'}
}

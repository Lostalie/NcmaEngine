# Trusted local package/review helpers only. Loading this file performs no IO,
# process launch, deployment, model call or credential lookup.
. (Join-Path $PSScriptRoot 'AiCandidatePackage.ps1')
. (Join-Path $PSScriptRoot 'CapturedBuild.ps1')

function Open-ExtensionPackageCapture([string]$Workspace,[string]$IndexPath) {
    $IndexPath=Assert-DeploymentPath $Workspace $IndexPath
    if((Get-Item -LiteralPath $IndexPath).Length -gt 65536){throw 'extension_index_budget'}
    $index=Get-Content -Raw -LiteralPath $IndexPath|ConvertFrom-Json
    if($null -eq $index.PSObject.Properties['capturedBuildRoot'] -or $null -eq $index.PSObject.Properties['captureManifestSha256']){throw 'extension_uncaptured_package'}
    $capture=Open-CapturedBuild $Workspace $index.capturedBuildRoot
    if($capture.ManifestSha256 -cne $index.captureManifestSha256 -or $capture.Manifest.sourceInputsSha256 -cne $index.sourceInputsSha256){$capture.Pins.Dispose();throw 'extension_capture_mismatch'}
    return $capture
}

function Read-ExtensionPackageSet([string]$Workspace,[string]$IndexPath,[string]$SourceHash) {
    $IndexPath=Assert-DeploymentPath $Workspace $IndexPath
    $generation=Split-Path -Parent $IndexPath
    $prefix=[IO.Path]::GetFullPath((Join-Path $Workspace 'out/package/m2-7'))+'\'
    if(-not $generation.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $IndexPath) -cne 'extensions-packages.json' -or
        (Get-Item -LiteralPath $IndexPath).Length -gt 65536){throw 'extension_index_path'}
    $index=Get-Content -Raw -LiteralPath $IndexPath|ConvertFrom-Json
    if($index.schemaVersion -ne 1 -or $index.configuration -cnotin @('Debug','Release') -or $index.tested -isnot [bool] -or $index.tested -or
        $index.production -isnot [bool] -or $index.production -or $SourceHash -cnotmatch '^[0-9A-F]{64}$' -or $index.sourceInputsSha256 -cne $SourceHash){throw 'extension_index_identity'}
    if((Split-Path -Leaf $generation) -cnotmatch '^[a-f0-9]{32}$' -or (Split-Path -Leaf (Split-Path -Parent $generation)) -cne $index.configuration){throw 'extension_index_generation'}
    $expected=@('editorExtensions','playerNullExtensions','playerDx11Extensions','playerNullPhysicsExtensions','playerDx11PhysicsExtensions')
    $actual=@($index.products.PSObject.Properties.Name)
    if($actual -contains 'editorAiExtensions'){$expected+=@('editorAiExtensions','playerNullAiExtensions')}
    if(@(Compare-Object $expected $actual -CaseSensitive).Count){throw 'extension_index_products'}
    foreach($name in $expected){
        $entry=$index.products.$name;$directory=Assert-DeploymentPath $Workspace $entry.path
        if($directory -cne (Join-Path $generation $name) -or $entry.manifestSha256 -cnotmatch '^[0-9A-F]{64}$' -or
            (Get-DeploymentHash (Join-Path $directory 'deployment-manifest.json')) -cne $entry.manifestSha256){throw 'extension_index_manifest'}
        $manifest=Read-DeploymentManifest $directory
        if($null -ne $index.PSObject.Properties['capturedBuildRoot'] -and
            ($manifest.capturedBuildRoot -cne $index.capturedBuildRoot -or $manifest.captureManifestSha256 -cne $index.captureManifestSha256)){throw 'extension_product_capture_mismatch'}
        $product=switch($name){
            'editorExtensions' {'NcmaEngine-editor-candidate'}
            'editorAiExtensions' {'NcmaEngine-ai-editor-candidate'}
            'playerDx11Extensions' {'NcmaPlayer-dx11-candidate'}
            'playerNullPhysicsExtensions' {'NcmaPlayer-null-physics-candidate'}
            'playerDx11PhysicsExtensions' {'NcmaPlayer-dx11-physics-candidate'}
            default {'NcmaPlayer-null-candidate'}
        }
        $ai=$name -ceq 'editorAiExtensions'
        if($manifest.schemaVersion -ne 1 -or $manifest.product -cne $product -or $manifest.configuration -cne $index.configuration -or
            $manifest.rid -cne 'win-x64' -or $manifest.tfm -cne 'net8.0' -or $manifest.publishMode -cne 'framework-dependent' -or
            $manifest.production -isnot [bool] -or $manifest.production -or $manifest.extensionsEnabled -isnot [bool] -or -not $manifest.extensionsEnabled -or
            $manifest.aiEnabled -isnot [bool] -or $manifest.aiEnabled -ne $ai -or $manifest.sourceInputsSha256 -cne $SourceHash){throw 'extension_manifest_identity'}
        $files=@(Get-DeploymentFiles $Workspace $directory);$listed=@($manifest.files)
        if($listed.Count -lt 1 -or $listed.Count -gt 8191 -or $files.Count -ne $listed.Count+1){throw 'extension_file_set'}
        $map=@{};foreach($file in $files){$map.Add($file.path,$file)};$seen=@{}
        foreach($file in $listed){
            $path=[string]$file.path
            if($path -ceq 'deployment-manifest.json' -or $seen.ContainsKey($path) -or -not $map.ContainsKey($path) -or
                $map[$path].path -cne $path -or $map[$path].size -ne $file.size -or $map[$path].sha256 -cne $file.sha256){throw 'extension_file_integrity'}
            $seen.Add($path,$true)
        }
        Assert-AiCandidateDependencies $Workspace $directory $ai $true
    }
    $pairs=@(@('editorExtensions','playerNullExtensions'),@('editorExtensions','playerDx11Extensions'),
        @('editorExtensions','playerNullPhysicsExtensions'),@('editorExtensions','playerDx11PhysicsExtensions'))
    if($expected -contains 'editorAiExtensions'){$pairs+=,@('editorAiExtensions','playerNullAiExtensions')}
    foreach($pair in $pairs){foreach($library in 'Ncma.Extensions.dll','Ncma.Extensions.Hosting.dll'){
        if((Get-DeploymentHash (Join-Path $index.products.($pair[0]).path $library)) -cne (Get-DeploymentHash (Join-Path $index.products.($pair[1]).path $library))){throw 'extension_editor_player_module_mismatch'}
    }}
    return $index
}

function Assert-ExtensionTestReceipt([string]$Workspace,[string]$ReceiptPath,[string]$SourceHash,[string]$IndexPath) {
    $ReceiptPath=Assert-DeploymentPath $Workspace $ReceiptPath
    if((Get-Item -LiteralPath $ReceiptPath).Length -gt 4194304){throw 'extension_receipt_budget'}
    $receipt=Get-Content -Raw -LiteralPath $ReceiptPath|ConvertFrom-Json
    if($receipt.kind -cne 'ncma.m8.5.acceptance.v1' -or $receipt.sourceInputsSha256 -cne $SourceHash -or
        $receipt.accepted -isnot [bool] -or -not $receipt.accepted -or $receipt.manualAcceptance -isnot [bool] -or -not $receipt.manualAcceptance){throw 'extension_receipt_unaccepted'}
    if($receipt.packageIndex -cne $IndexPath -or $receipt.packageIndexSha256 -cne (Get-DeploymentHash $IndexPath)){throw 'extension_receipt_package'}
    $configs=@($receipt.configurations)
    if($configs.Count -ne 2 -or @($configs|Where-Object configuration -CEQ 'Debug').Count -ne 1 -or @($configs|Where-Object configuration -CEQ 'Release').Count -ne 1){throw 'extension_receipt_configurations'}
    $capture=Open-ExtensionPackageCapture $Workspace $IndexPath
    try {
    if($receipt.captureManifestSha256 -cne $capture.ManifestSha256){throw 'extension_receipt_capture'}
    $projects=@($capture.Manifest.files|Where-Object{$_.path.EndsWith('.csproj',[StringComparison]::Ordinal)}|ForEach-Object{$_.path})
    foreach($config in $configs){
        if($config.buildWarnings -ne 0 -or $config.buildErrors -ne 0 -or $config.projectCount -ne $projects.Count -or
            @(Compare-Object $projects @($config.projects) -CaseSensitive).Count -or $config.expectedTests -le 0 -or $config.executedTests -ne $config.expectedTests -or $config.failed -ne 0 -or $config.skipped -ne 0){throw 'extension_receipt_gates'}
        $evidence=@($config.evidence)
        if($evidence.Count -lt 3 -or $evidence.Count -gt 2048){throw 'extension_receipt_evidence'}
        foreach($file in $evidence){$path=Assert-DeploymentPath $Workspace $file.path;if((Get-DeploymentHash $path) -cne $file.sha256){throw 'extension_receipt_changed'}}
        # Counts alone are not test evidence. Match the complete CTest inventory
        # against actual JUnit cases and a source-bound full build report.
        foreach($required in 'buildReport','inventory','junit'){
            $path=Assert-DeploymentPath $Workspace $config.$required
            if(@($evidence|Where-Object path -CEQ $path).Count -ne 1 -or (Get-Item -LiteralPath $path).Length -gt 16777216){throw 'extension_receipt_report'}
        }
        $build=Get-Content -Raw -LiteralPath $config.buildReport|ConvertFrom-Json
        if($build.sourceInputsSha256 -cne $SourceHash -or $build.captureManifestSha256 -cne $capture.ManifestSha256 -or $build.configuration -cne $config.configuration -or
            $build.exitCode -ne 0 -or $build.warnings -ne 0 -or $build.errors -ne 0 -or
            @(Compare-Object $projects @($build.projects) -CaseSensitive).Count){throw 'extension_receipt_build'}
        $inventory=Get-Content -Raw -LiteralPath $config.inventory|ConvertFrom-Json
        $names=@($inventory.tests|ForEach-Object{$_.name})
        if($names.Count -ne $config.expectedTests -or @($names|Sort-Object -Unique).Count -ne $names.Count){throw 'extension_receipt_inventory'}
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $reader=[Xml.XmlReader]::Create($config.junit,$settings)
        try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}
        $cases=@($xml.SelectNodes('//testcase'));$actual=@($cases|ForEach-Object{$_.GetAttribute('name')})
        if($cases.Count -ne $names.Count -or @($actual|Sort-Object -Unique).Count -ne $actual.Count -or
            @(Compare-Object $names $actual -CaseSensitive).Count){throw 'extension_receipt_test_set'}
        foreach($case in $cases){
            if($case.GetAttribute('status') -cne 'run' -or $null -ne $case.SelectSingleNode('failure|error|skipped')){throw 'extension_receipt_test_failed'}
        }
    }
    # A trusted local evidence record, not a signature or permission for an AI tool.
    return $receipt
    } finally { $capture.Pins.Dispose() }
}

function Get-ExtensionInstallReview([string]$Workspace,[string]$IndexPath,[string]$ReceiptPath,[bool]$EnableAI) {
    $capture=Open-ExtensionPackageCapture $Workspace $IndexPath
    try {
    $sourceHash=$capture.Manifest.sourceInputsSha256
    $index=Read-ExtensionPackageSet $Workspace $IndexPath $sourceHash
    $null=Assert-ExtensionTestReceipt $Workspace $ReceiptPath $sourceHash $IndexPath
    $name=if($EnableAI){'editorAiExtensions'}else{'editorExtensions'}
    if($null -eq $index.products.PSObject.Properties[$name]){throw 'extension_selected_product_missing'}
    $entry=$index.products.$name
    $old=@(Get-DeploymentFiles $Workspace (Join-Path $Workspace 'out/bin')|Where-Object path -eq 'deployment-manifest.json')
    if($old.Count -ne 1){throw 'extension_existing_install_missing'}
    $text="ncma.extension.install-review.v1`n$sourceHash`n$($entry.manifestSha256)`n$($old[0].sha256)`n$(Get-DeploymentHash $ReceiptPath)`n$EnableAI"
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','')}finally{$hash.Dispose()}
    [pscustomobject]@{fingerprint=$fingerprint;package=$entry.path;candidateManifestSha256=$entry.manifestSha256;oldManifestSha256=$old[0].sha256;
        sourceInputsSha256=$sourceHash;configuration=$index.configuration;enableAI=$EnableAI;requiresNormalClose=$true;automaticRestart=$false}
    } finally { $capture.Pins.Dispose() }
}

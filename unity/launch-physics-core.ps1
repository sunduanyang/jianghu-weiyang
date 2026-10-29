param([ValidateSet('core','legacy')][string]$Backend='core')
$ErrorActionPreference='Stop'
$candidateFolder=Join-Path $PSScriptRoot 'Build\PhysicsCoreCurrent20261029'
$candidatePlayers=@(Get-ChildItem -LiteralPath $candidateFolder -Filter '*.exe' -File | Where-Object Name -ne 'UnityCrashHandler64.exe')
if($candidatePlayers.Count -ne 1){throw 'The Physics Core comparison client is not built.'}
$candidateRuntime=Join-Path $candidateFolder ($candidatePlayers[0].BaseName+'_Data\StreamingAssets\Python\python.exe')
if(!(Test-Path -LiteralPath $candidateRuntime)){throw 'The bundled Python runtime is missing.'}
$candidateReview=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\output\physics-core-review\'+$Backend)))
$candidateSave=Join-Path $candidateReview 'save'
New-Item -ItemType Directory -Force $candidateSave | Out-Null
Start-Process -FilePath $candidatePlayers[0].FullName -WindowStyle Normal -ArgumentList @(
    '-force-d3d11','-combatPhysics',$Backend,'-saveDir',('"'+$candidateSave+'"'),
    '-python',('"'+$candidateRuntime+'"'),'-logFile',('"'+(Join-Path $candidateReview 'game.log')+'"'))

$ErrorActionPreference = 'Stop'
# A different view of the same current game build, with no separate combat
# release. Normal play and the sandbox always receive the same combat code.
& (Join-Path $PSScriptRoot 'launch-game.ps1') -referenceCombat 1 -screen-fullscreen 0 -screen-width 1600 -screen-height 950

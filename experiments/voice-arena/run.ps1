param(
    [Parameter(Mandatory)][ValidateSet('dev','heldout')][string]$Split,
    [Parameter(Mandatory)][ValidateSet('incumbent','sendable','copy-editor')][string]$Variant,
    [ValidateRange(0, 2048)][int]$ReasoningBudget = 0
)
$name = "$Split-$Variant"
if ($ReasoningBudget -gt 0) { $name += "-thinking-$ReasoningBudget" }
$arguments = @('--no-build','--project','tools/Shruti.QualityBench','--',"artifacts/quality/$Split.json",'--stage','refinement','--name',$name,'--reasoning-budget',$ReasoningBudget,'--cleanup-model','artifacts/local-cleanup/qwen3.5-2b-q4_k_m.gguf','--cleanup-runtime','artifacts/local-cleanup/Cpu/llama-server.exe')
if ($Variant -ne 'incumbent') { $arguments += @('--cleanup-prompt',"experiments/voice-arena/$Variant.txt") }
& dotnet run @arguments
exit $LASTEXITCODE

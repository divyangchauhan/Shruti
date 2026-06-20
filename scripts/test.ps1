param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$testClasses = @(
    "DictationCoordinatorTests",
    "DictationSessionStateTests",
    "DictationShellControllerTests",
    "DictationTriggerRouterTests",
    "JsonSettingsRepositoryTests",
    "SqliteSessionRepositoryTests",
    "TranscriptExportServiceTests",
    "WindowsAudioCaptureServiceTests",
    "WindowsGlobalTriggerServiceTests",
    "WindowsTargetFocusServiceTests",
    "WindowsTextInsertionServiceTests",
    "WindowsTrayIconServiceTests"
)

foreach ($testClass in $testClasses) {
    dotnet test tests/Shruti.Tests/Shruti.Tests.csproj `
        --configuration $Configuration `
        --no-build `
        --filter "FullyQualifiedName~$testClass" `
        --logger "console;verbosity=normal" `
        -p:Platform=x64

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

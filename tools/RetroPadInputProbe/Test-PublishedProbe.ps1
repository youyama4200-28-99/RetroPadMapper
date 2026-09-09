param(
    [Parameter(Mandatory = $true)][string]$Executable
)
$ErrorActionPreference = 'Stop'
$probeExe = (Resolve-Path -LiteralPath $Executable).Path
foreach ($argument in @('--self-test', '--self-test', '--pipe-test')) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($probeExe, $argument)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $child = [System.Diagnostics.Process]::Start($start)
    try {
        $stdoutTask = $child.StandardOutput.ReadToEndAsync()
        $stderrTask = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(15000)) {
            $child.Kill()
            throw 'Diagnostic test child exceeded 15 seconds.'
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($child.ExitCode -ne 0) { throw "Test $argument failed: exit $($child.ExitCode), $stderr" }
        if ($argument -eq '--pipe-test') {
            if ($stdout.TrimEnd() -cne '入力確認：コンソールなし' -or $stderr.TrimEnd() -cne '診断確認：標準エラー') {
                throw 'UTF-8 pipe round-trip mismatch.'
            }
        } elseif ($stderr.Length -ne 0) { throw "Unexpected stderr: $stderr" }
        Write-Output "PASS $argument (published EXE, CreateNoWindow, UTF-8 pipes)"
    } finally {
        if (-not $child.HasExited) { $child.Kill() }
        $child.Dispose()
    }
}

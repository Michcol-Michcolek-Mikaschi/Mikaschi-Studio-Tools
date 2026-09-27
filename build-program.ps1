[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$buildRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'build'))
$outputDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $buildRoot 'Mikaschi-Studio-Tools-win-x64'))
$projectPath = Join-Path $repositoryRoot 'src\NarzedziaHost\NarzedziaHost.csproj'
$solutionPath = Join-Path $repositoryRoot 'src\Narzedzia.slnx'

$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$expectedOutputPrefix = $buildRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (!$buildRoot.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    [System.IO.Path]::GetFileName($buildRoot) -ne 'build') {
    throw "Katalog wynikowy musi być bezpośrednim katalogiem build repozytorium: $repositoryRoot"
}

if (!$outputDirectory.StartsWith($expectedOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Katalog publikacji musi znajdować się wewnątrz: $buildRoot"
}

# Usuwamy wyłącznie znane katalogi generowane. Inne dane umieszczone w `build`
# (np. mapy diagnostyczne) nie należą do publikacji i muszą zostać zachowane.
$generatedDirectories = @(
    [System.IO.Path]::GetFullPath((Join-Path $buildRoot 'bin')),
    [System.IO.Path]::GetFullPath((Join-Path $buildRoot 'obj')),
    $outputDirectory
)

foreach ($directory in $generatedDirectories) {
    if (!$directory.StartsWith($expectedOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Odmowa usunięcia katalogu spoza bezpiecznego katalogu wynikowego: $directory"
    }

    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if ($Test) {
    & dotnet test $solutionPath --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Testy zakończyły się kodem $LASTEXITCODE. Publikacja została przerwana."
    }
}

& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $outputDirectory `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Publikacja zakończyła się kodem $LASTEXITCODE."
}

$publishedExecutablePath = Join-Path $outputDirectory 'NarzedziaHost.exe'
$executablePath = Join-Path $outputDirectory 'Mikaschi Studio Tools.exe'
if (!(Test-Path -LiteralPath $publishedExecutablePath)) {
    throw "Publikacja nie utworzyła oczekiwanego pliku apphost: $publishedExecutablePath"
}

# Zachowujemy wewnętrzną nazwę assembly NarzedziaHost, ponieważ korzystają z niej
# pliki runtimeconfig/deps i URI zasobów Avalonia. Zmieniamy wyłącznie przyjazną
# nazwę końcowego pliku uruchamianego przez użytkownika.
Move-Item -LiteralPath $publishedExecutablePath -Destination $executablePath
if (!(Test-Path -LiteralPath $executablePath)) {
    throw "Nie udało się nadać programowi docelowej nazwy: $executablePath"
}

# Po sprawdzeniu publikacji pośrednie pliki kompilatora nie są już potrzebne.
# Dzięki temu istnieje tylko jedna uruchamialna kopia bieżącej wersji programu.
foreach ($directory in $generatedDirectories) {
    if ($directory -ne $outputDirectory -and (Test-Path -LiteralPath $directory)) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

Write-Host "Gotowe: $executablePath" -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory
}

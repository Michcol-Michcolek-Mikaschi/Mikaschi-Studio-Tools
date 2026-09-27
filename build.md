# Kompilacja programu

Wszystkie wyniki rozwiązania trafiają wyłącznie do katalogu `build`. Gotowa,
samodzielna aplikacja Windows zawsze zastępuje poprzednią wersję w katalogu:

`build/Mikaschi-Studio-Tools-win-x64`

## Pełna kompilacja

Uruchom polecenie z katalogu głównego repozytorium:

```powershell
.\build-program.ps1 -Test
```

Skrypt wykonuje pełny zestaw testów, publikuje aplikację self-contained dla
Windows x64 i usuwa pośrednie katalogi `build/bin` oraz `build/obj`.

Sama publikacja bez ponownego uruchamiania testów:

```powershell
.\build-program.ps1
```

## Uruchomienie

Plik wykonywalny:

`build/Mikaschi-Studio-Tools-win-x64/Mikaschi Studio Tools.exe`

Publikacja i uruchomienie w jednym kroku:

```powershell
.\build-program.ps1 -Run
```

## Szybka praca deweloperska

```powershell
dotnet build .\src\Narzedzia.slnx
dotnet test .\src\Narzedzia.slnx
```

Sprawdzenie znanych podatności pakietów NuGet:

```powershell
dotnet list .\src\Narzedzia.slnx package --vulnerable --include-transitive
```

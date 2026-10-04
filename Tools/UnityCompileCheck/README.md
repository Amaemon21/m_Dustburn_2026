# UnityCompileCheck

Компиляция генерации мира против **настоящих** сборок Unity и Repetitionless, без запуска
редактора. Проектов столько же, сколько asmdef-сборок, и ссылки у них те же:

| проект | сборка | исходники |
|---|---|---|
| `UnityCompileCheck.Utils.csproj` | `Dustborn.Utils` | `Assets/_Dustborn/Scripts/Utils/` |
| `UnityCompileCheck.Runtime.csproj` | `Dustborn.WorldGen` | `Scripts/Gameplay/Service/WorldService/` без `Editor/` |
| `UnityCompileCheck.Editor.csproj` | `Dustborn.WorldGen.Editor` | `WorldService/Editor/` |
| `UnityCompileCheck.Benchmarks.csproj` | `Dustborn.WorldGen.Benchmarks` | `Benchmarks/WorldGeneration/` без `Editor/` |
| `UnityCompileCheck.BenchmarksEditor.csproj` | `Dustborn.WorldGen.Benchmarks.Editor` | `Benchmarks/WorldGeneration/Editor/` |

```
dotnet build Tools/UnityCompileCheck/UnityCompileCheck.BenchmarksEditor.csproj -v q
```

собирает все пять по ссылкам проектов; `Tools/UnityTestCompileCheck` собирает тесты поверх
них. Общие ссылки на DLL — в `UnityReferences.props`.

Зачем он рядом с `HeadlessCheck`: тот подменяет Unity заглушками и поэтому исключает всё,
что цепляет движок целиком — `VoxelTerrainBuilder`, `RepetitionlessVoxelSetup`, текстурные
генераторы. Здесь исключений нет: берутся DLL из установленной Unity и из
`Library/ScriptAssemblies/`, так что опечатка в вызове API Repetitionless ловится до
перекомпиляции в редакторе, а ссылка через границу сборки — так же, как её поймала бы Unity.
Проверяются только типы — ничего не исполняется.

Пути можно переопределить:

```
dotnet build Tools/UnityCompileCheck/UnityCompileCheck.BenchmarksEditor.csproj -p:UnityManaged="D:\Unity\6000.3.20f1\Editor\Data\Managed"
```

`Library/ScriptAssemblies/` в репозитории нет — Unity должна собрать проект хотя бы раз.

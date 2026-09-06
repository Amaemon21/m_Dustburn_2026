# UnityCompileCheck

Компиляция `Assets/_Dustborn/Scripts/WorldGeneration/` против **настоящих** сборок Unity
и Repetitionless, без запуска редактора.

```
dotnet build Tools/UnityCompileCheck -v q
```

Зачем он рядом с `HeadlessCheck`: тот подменяет Unity заглушками и поэтому исключает всё,
что цепляет движок целиком — `WorldTerrainBuilder`, `RepetitionlessTerrainSetup`, текстурные
генераторы. Здесь исключений нет: берутся DLL из установленной Unity и из
`Library/ScriptAssemblies/`, так что опечатка в вызове API Repetitionless или Terrain ловится
до перекомпиляции в редакторе. Проверяются только типы — ничего не исполняется.

Пути можно переопределить:

```
dotnet build Tools/UnityCompileCheck -p:UnityManaged="D:\Unity\6000.3.20f1\Editor\Data\Managed"
```

`Library/ScriptAssemblies/` в репозитории нет — Unity должна собрать проект хотя бы раз.

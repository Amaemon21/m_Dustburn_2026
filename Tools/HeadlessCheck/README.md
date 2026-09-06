# HeadlessCheck

Компиляция и прогон слоёв генерации мира без Unity.

`Stubs.cs` — заглушка Unity API (UnityEngine, UnityEditor, Unity.Mathematics.Random, NaughtyAttributes).
`Missing.cs` — пустышки для генераторов на Burst/Unity.Mathematics, которые заглушкой не покрыты.
`AssetReader.cs` — чтение настоящих `.asset` (конфиг, профили рангов с их составом, биомы, PoiDatabase) вместо дефолтов из кода.
`SimplexNoise.cs` — порт Ashima snoise на место `Unity.Mathematics.noise.snoise`.
`Harness.cs` — прогон конвейера: BiomeMapGenerator -> HeightMapGenerator -> HubPlacer -> RoadPlanner
              -> TerrainCarver -> CityPlanner -> PoiPlacer.
`Draw.cs`, `Png.cs` — рендер результата в PNG.

`JobStubs.cs` — заглушка Unity.Burst / Unity.Jobs / Unity.Collections, чтобы джобы и painter-ы террейна
тоже компилировались (исполняются они только в Unity).

Подключается и `Assets/_Dustborn/Scripts/Utils/` — там `DistanceUtility`, которым пользуется генерация.

В конце прогона печатается время по этапам. Читать его как «где вообще дорого», а не как замер Unity:
`Schedule` в заглушке крутит `Execute` в одном потоке и без Burst, поэтому `рельеф` и `биомы` завышены.

Файлы из `Assets/_Dustborn/Scripts/WorldGeneration/` подключаются напрямую, не копиями,
так что сборка ловит ошибки компиляции в боевом коде.

```
dotnet run -c Debug
dotnet run -c Debug -- путь/к/HeightMap.bytes
```

По умолчанию читается `Assets/_Dustborn/Generated/HeightMap.bytes` — тот, что писал шаг 2
генератора. Рядом появляются `unity_world.png`, `unity_city0.png`, `unity_zoom.png`,
`unity_bare.png`.

Любое поле `WorldGenerationConfig` переопределяется аргументом `Имя=значение`, поле профиля ранга — через точку (`TownProfile.RadiusScale=0.66`), так что
подбирать плотность застройки можно без Unity:

```
dotnet run -c Debug -- HubCount=6 MinHubDistance=650 LotMargin=4
dotnet run -c Debug -- путь/к/HeightMap.bytes CityRadiusScale=1.2
```

Заглушка не заменяет Unity: она проверяет типы и арифметику, а не поведение движка.
Всё, что упирается в Terrain, Texture2D или импорт ассетов, здесь пустое.

# HeadlessCheck

Компиляция и прогон слоёв генерации мира без Unity.

`Stubs.cs` — заглушка Unity API (UnityEngine, UnityEditor, Unity.Mathematics.Random, NaughtyAttributes).
`Missing.cs` — пустышки для генераторов на Burst/Unity.Mathematics, которые заглушкой не покрыты.
`AssetReader.cs` — чтение настоящих `.asset` (конфиг, биомы, PoiDatabase) вместо дефолтов из кода.
`SimplexNoise.cs` — порт Ashima snoise на место `Unity.Mathematics.noise.snoise`.
`Harness.cs` — прогон конвейера: BiomeMapGenerator -> HeightMapGenerator -> HubPlacer -> RoadGraph
              -> SettlementPlanner -> TerrainCarver -> RoadPlanner -> PoiPlacer.
`RoadPaintChecks.cs` — покраска дорог: прямая кромка под любым углом к сетке контрольных текселей.
`Draw.cs`, `Png.cs` — рендер результата в PNG.

`JobStubs.cs` — заглушка Unity.Burst / Unity.Jobs / Unity.Collections, чтобы джобы и painter-ы террейна
тоже компилировались (исполняются они только в Unity).

Подключается и `Assets/_Dustborn/Scripts/Utils/` — там `DistanceUtility`, которым пользуется генерация.

В конце прогона печатается время по этапам. Читать его как «где вообще дорого», а не как замер Unity:
`Schedule` в заглушке крутит `Execute` в одном потоке и без Burst, поэтому `рельеф` и `биомы` завышены.

Файлы из `Assets/_Dustborn/Scripts/Gameplay/Service/WorldService/` подключаются напрямую, не копиями,
так что сборка ловит ошибки компиляции в боевом коде.

```
dotnet run -c Release
dotnet run -c Release -- --road-paint
dotnet run -c Release -- --settlements-only --raw-cache путь/к/raw.bytes
```

Рядом появляются `unity_world.png`, `unity_settlement_large.png`, `unity_settlement_medium.png`,
`unity_settlement_small.png`, `unity_zoom.png`, `unity_bare.png`.

`--settlements-only` останавливается после поселений, дорог и зданий, пропуская воксели и декор.
`--raw-cache` сохраняет сырую карту высот в файл при первом прогоне и читает её при следующих:
рельеф — это почти всё время прогона. Кеш не знает, с какими настройками рельефа он сделан,
так что после правки рельефа или сида файл надо удалить.

Любое поле `WorldGenerationConfig` переопределяется аргументом `Имя=значение`, так что
подбирать плотность застройки можно без Unity:

```
dotnet run -c Release -- --settlements-only --raw-cache raw.bytes MaxHouses=160 BlockSizeMin=56
dotnet run -c Release -- --settlements-only --raw-cache raw.bytes StreetLoopChance=0.6 LotMargin=4
```

Заглушка не заменяет Unity: она проверяет типы и арифметику, а не поведение движка.
Всё, что упирается в Terrain, Texture2D или импорт ассетов, здесь пустое.

# World generation performance run

Results directory: `E:\src\m_Dustburn_2026\BenchmarkResults\baseline`

## Coverage

| status | specifications |
|---|---|
| blocked_dependency | 1 |
| blocked_resource | 2 |
| not_run | 31 |
| passed | 74 |

Total specifications: 108

## Slowest measured cases

| case | p50 ms | max ms | samples | status |
|---|---|---|---|---|
| MAT-06[resolution=256] | 7553.767 | 7553.767 | 1 | passed |
| VOX-10[allowFullPreview=true] | 4493.275 | 4493.275 | 1 | passed |
| LIFE-06[allowFullPreview=true] | 4082.727 | 4082.727 | 1 | passed |
| SET-13[segments=10000] | 3287.228 | 3317.847 | 3 | passed |
| MAT-05[resolution=256] | 970.507 | 970.507 | 1 | passed |
| SET-13[segments=1000] | 336.083 | 338.363 | 3 | passed |
| IO-05 | 331.064 | 331.064 | 1 | passed |
| DEC-02[points=100000] | 305.764 | 307.552 | 3 | passed |
| IO-07 | 172.004 | 172.004 | 1 | passed |
| HGT-09[world=2048;cell=1] | 170.587 | 171.744 | 3 | passed |
| MAT-02[resolution=2048] | 145.598 | 147.869 | 3 | passed |
| VOX-02[morph=0] | 73.803 | 74.033 | 3 | passed |
| VOX-02[morph=1] | 72.936 | 73.613 | 3 | passed |
| HGT-09[world=2048;cell=2] | 45.912 | 46.898 | 3 | passed |
| IO-06 | 41.833 | 41.833 | 1 | passed |
| IO-04[megabytes=8;existing=true] | 39.137 | 39.137 | 1 | passed |
| MAT-02[resolution=1024] | 37.862 | 38.73 | 3 | passed |
| IO-04[megabytes=8] | 27.864 | 27.864 | 1 | passed |
| BIO-06[points=100000] | 21.17 | 21.743 | 3 | passed |
| LIFE-02[stopAt=6] | 19.328 | 19.328 | 1 | passed |

## Largest measured main thread stalls

One row per instrumented stage, taken from the worst single occurrence recorded for it.

| stage | worst ms | case | occurrences |
|---|---|---|---|
| PreviewGeometry | 1941.77 | VOX-10[allowFullPreview=true] | 2 |
| PreviewDecor | 1014.441 | VOX-10[allowFullPreview=true] | 2 |
| MapHeightNoise | 163.077 | HGT-09[world=2048;cell=1] | 17 |
| PreviewClear | 45.29 | LIFE-06[allowFullPreview=true] | 6 |
| DecorStep | 19.923 | VOX-10[allowFullPreview=true] | 897 |
| MapTotal | 19.511 | LIFE-01[stopAt=1] | 3 |
| MapHeights | 10.158 | LIFE-02[stopAt=4] | 7 |
| MapHeightHydraulic | 8.606 | LIFE-02[stopAt=4] | 17 |
| MapCities | 6.599 | LIFE-01[stopAt=4] | 5 |
| DecorPlace | 4.314 | VOX-10[allowFullPreview=true] | 11648 |
| DecorCombine | 3.748 | VOX-10[allowFullPreview=true] | 852 |
| MapWeights | 3.022 | HGT-09[world=2048;cell=1] | 17 |

## Managed allocation, largest cases

Current thread managed allocations only. This is not process memory and not native memory.

| case | alloc p50 bytes | alloc max bytes |
|---|---|---|

## Specifications without a result

| id | mode | status | reason |
|---|---|---|---|
| POI-06 | PM | not_run | no result recorded in this run |
| POI-07 | PM | not_run | no result recorded in this run |
| IO-08 | ED | blocked_dependency | A full bake writes into the project Generated folder. Pass allowSourceWrite=true to opt in. |
| VOX-09 | PM | not_run | no result recorded in this run |
| VOX-10 | ED | blocked_resource | A whole world preview of 8192 m is estimated at 1,3 M triangles, 0,7 M vertices, 70 MB with colliders over 448 columns, against a 512 MiB geometry budget, and the maps and decor are on top of that. Pass allowFullPreview=true to run it deliberately rather than let a batch suite trip over it. |
| QUE-01 | PM | not_run | no result recorded in this run |
| QUE-02 | PM | not_run | no result recorded in this run |
| QUE-03 | PM | not_run | no result recorded in this run |
| QUE-04 | PM | not_run | no result recorded in this run |
| QUE-05 | PM | not_run | no result recorded in this run |
| QUE-06 | PM | not_run | no result recorded in this run |
| QUE-07 | PM | not_run | no result recorded in this run |
| QUE-08 | PM | not_run | no result recorded in this run |
| DEC-03 | PM | not_run | no result recorded in this run |
| DEC-05 | PM | not_run | no result recorded in this run |
| DEC-06 | PM | not_run | no result recorded in this run |
| DEC-07 | PM | not_run | no result recorded in this run |
| DEC-08 | PM | not_run | no result recorded in this run |
| DEC-09 | PM | not_run | no result recorded in this run |
| E2E-01 | PL | not_run | no result recorded in this run |
| E2E-02 | PL | not_run | no result recorded in this run |
| E2E-03 | PL | not_run | no result recorded in this run |
| E2E-04 | PL | not_run | no result recorded in this run |
| E2E-05 | PL | not_run | no result recorded in this run |
| E2E-06 | PL | not_run | no result recorded in this run |
| E2E-07 | PL | not_run | no result recorded in this run |
| E2E-08 | PL | not_run | no result recorded in this run |
| E2E-09 | PL | not_run | no result recorded in this run |
| E2E-10 | PL | not_run | no result recorded in this run |
| E2E-11 | PL | not_run | no result recorded in this run |
| LIFE-03 | PL | not_run | no result recorded in this run |
| LIFE-04 | PL | not_run | no result recorded in this run |
| LIFE-06 | ED | blocked_resource | A whole world preview of 8192 m is estimated at 1,3 M triangles, 0,7 M vertices, 70 MB with colliders over 448 columns, against a 512 MiB geometry budget, and the maps and decor are on top of that. Pass allowFullPreview=true to run it deliberately rather than let a batch suite trip over it. |
| LIFE-07 | PM | not_run | no result recorded in this run |


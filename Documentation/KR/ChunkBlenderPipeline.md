# Chunk 및 Blender 파이프라인

## 데이터 흐름

```text
Blender Authoring
  -> chunk-local FBX + manifest v2 + placements v1
  -> Unity manifest/hash/좌표 검증
  -> Chunk Prefab
  -> Region Prefab + DirectRegionCatalog
  -> ChunkStreamingService
```

`WorldGridSettings`가 World ID, 128m Authoring Chunk, 4x4 Chunk Region, Query Cell 크기, World Origin의 유일한 원본입니다. Blender Add-on 설정은 이 asset과 같은 값을 사용해야 하며 importer가 차이를 오류로 보고합니다.

## 소유권 규칙

- 좌표 구간은 `[min, max)`입니다.
- 음수 좌표는 truncation이 아니라 floor division을 사용합니다.
- 작은 instance와 marker는 pivot 청크가 소유합니다.
- 정적 geometry/collision은 소유 청크의 X/Z 범위 안에 있어야 합니다.
- 청크를 넘는 지형은 Blender에서 분할합니다. 자동 절단은 authoring 토폴로지와 normal seam 정책이 필요한 별도 도구입니다.
- 거대 랜드마크는 `GLOBAL` 또는 명시적인 Region 단위 asset으로 분리합니다.

## 수직 저작 레이어

세로로 긴 맵을 다루기 위한 **저작 전용** 개념입니다. 런타임 좌표계는 그대로 2D 청크입니다.

- Blender Z를 `layer_base_z + index * layer_height` 로 균일하게 나눕니다. 청크와 동일한 `[min, max)` floor 규칙입니다.
- 레이어는 청크 소유권, Region 계산, 스트리밍에 전혀 영향을 주지 않습니다.
- `placements.json`의 각 레코드에 `layer` 정수만 기록되고, Unity는 이를 `ChunkEntityPlacement.AuthoringLayer`로 보존합니다.
- 오브젝트에 `Layer Override`를 켜면 Z 위치 대신 지정한 인덱스를 사용합니다.

Blender `수직 레이어` 패널에서 활성 레이어 이동, 격리 보기, 선택 항목 스냅/층 이동, 시점 이동을 처리합니다. `그리드가 레이어 따라감`을 켜면 청크 그리드 오버레이가 활성 레이어 바닥 높이에 그려지므로 위층에서도 청크 경계를 보면서 작업할 수 있습니다.

## 엔티티 배치

Blender `조형물 라이브러리`에서 에셋의 `배치 종류`를 `ENTITY`로 등록하면 배치 결과의 role이 `INSTANCE`가 아니라 `ENTITY`가 됩니다.

- 레코드에 `entity` 블록(`prefabId`, `kind`, `flags`, `lifetimeSeconds`)이 함께 기록됩니다.
- Unity importer는 `BlenderAssetRegistry` prefab에 `WorldEntityAuthoring`이 있는지, `PrefabId`가 Blender의 `prefabId`와 같은지 검증합니다. 다르면 임포트가 오류로 중단됩니다.
- 통과한 배치는 Chunk Prefab 아래 `Entities` 노드에 모이고 `ChunkEntityPlacement`가 붙습니다. 이 노드를 SubScene에 넣으면 그대로 DOTS 엔티티로 베이크됩니다.

## Unity 설정

1. `WorldGridSettings` asset을 생성합니다.
2. `BlenderAssetRegistry`에 Blender `INSTANCE`/`ENTITY`의 asset ID와 Prefab을 등록합니다. `ENTITY` prefab에는 `WorldEntityAuthoring`이 있어야 합니다.
3. `BlenderBridgeSettings`에 Grid, source root, generated root, registry를 지정합니다.
4. `WorldBuilder > Blender Bridge > Import All Chunks`를 실행합니다.
5. 생성된 `DirectRegionCatalog`를 `DirectReferenceRegionLoader`에 전달합니다.
6. `WorldBuilder > 월드 > 엔티티 카탈로그`로 `WorldEntityRuntimeAuthoring` 카탈로그와 임포트된 엔티티 배치를 대조합니다.

Importer는 manifest 버전, world/grid/region 계약, 상대 경로, 파일 크기/SHA-256, placements stable ID와 matrix를 검증합니다. 동일 source hash의 Chunk Prefab은 재생성하지 않습니다.

## 런타임 원칙

Region Prefab만 스트리밍 단위이며 각 Chunk Prefab은 Region-local 위치에 놓입니다. 각 청크마다 Update를 실행하지 않습니다. Water Query는 같은 WorldGrid의 Query Cell을 사용하지만 streaming/rendering과 독립된 compact array 데이터입니다.

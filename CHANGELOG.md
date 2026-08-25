# Changelog

## 0.9.0 — Underwater & Caves

### 절차적 동굴 생성 엔진

* `FbmNoise.Value3D` — 시드 결정형 3D fBm 추가
* `CaveField` — 워핑된 스파게티 터널 밴드(2축 |noise| 교차) + 심도 바이어스가 있는 케버른 룸 필드로
  기존 밀도 스토어를 **감산 카빙** — 하이트필드·침식·런타임 굴착과 자연스럽게 합성
* `CaveShapeParams` SO — 수직 범위(minY/maxY), 지표 보호 두께, 터널 폭/굽이/수직 스쿼시,
  룸 크기/문턱/심도 바이어스 전량 데이터화
* **동굴 프리셋 4종**: LimestoneCaves / LavaTubes / FloodedGrotto / AbyssalNetwork 원클릭 적용
* Terrain Forge "⑥ Caves" 섹션 — Generate 중 자동 카빙 + "Carve Caves Only" 단독 실행(자동 리베이크)

### 수중/지하 환경 분류

* `UndergroundProbe` — 물리 없이 복셀 밀도 상향 마칭으로 천장 여부·공기 갭·표면 심도 산출
* `EnvironmentClassifier` — 물 쿼리 + 인클로저 프로브 결합 한 번의 호출로
  OpenAir / Underwater / Underground / **FloodedCave**(수중 동굴) 판정

### 해저·동굴 바이옴 확장

* `BiomeType` 신규 4종: **Cave / CoralReef / KelpForest / AbyssalTrench** (기존 id 유지, 하위 호환)
* Whittaker 분류기 해저 밴드 세분화(해빈~해구 5단 계층) + `FromEnvironment`(밀폐 지점 → Cave)
* `SplatBaker` 기본 매핑에 신규 바이옴 추가(해저=seabed 레이어, 동굴=rock 레이어)

### 테스트

* 동굴 카빙(표면 보존·결정론·범위 준수·프리셋), 인클로저 프로브(천장·매몰·개방),
  환경 분류기 4영역 판정, 해저 바이옴 왕복·디버그 컬러 유니크 테스트 추가

## 0.8.0 — Terrain Visuals

### 스플랫맵 셰이더 + 자동 머티리얼

* `SplatBaker` — HighResBiomeMap → 청크별 4채널 스플랫맵(바이옴→레이어 매핑 설정 가능, 경계 이중선형 블렌딩)
* `WorldBuilder/TerrainSplat` URP 셰이더 — 4레이어 텍스처 블렌딩 + **버텍스 컬러 G 채널로 침식 지역 자동 노출**
* Terrain Forge: 텍스처 슬롯 지정 시 청크당 `_Control` 스플랫맵·머티리얼 에셋을 자동 생성/갱신

### 지형 LOD 체인

* `LODMeshSimplifier`가 버텍스 컬러를 보존하도록 확장
* 베이크 시 LOD1/LOD2 메시 자동 생성 + 조립된 청크에 `LODGroup` 구성(60%/25%/8% 화면 비율)

### 침식 강도 맵

* `ErosionSimulator.Apply`가 셀별 순 변화량(out erosionMap)을 리포트
* Generate 종료 시 R=침식/G=퇴적 그레이스케일 PNG(`erosion_map.png`) 출력 — 스플랫 레이어 가중치·프리팹 규칙 입력으로 활용 가능

### 테스트

* 스플랫맵 집중도·경계 블렌딩, 침식 맵 결정론·유의미한 이동량, LOD 축소+컬러 보존 테스트 4건 추가

## 0.7.0 — Performance & Power

### 성능

* **샘플러 제로카프 캐싱**: `VoxelWorldSampler`가 청크 엔트리를 캐시하고 밀도 배열을 직접 읽음
  — 이전에는 샘플 1회당 4KB(16³ float)를 복사 (메싱/생성 경로 수백 배 호출 감소)
* **병렬 메시 베이크**: `SurfaceNetsMesher`를 순수 지오메트리 패스(`ComputeGeometry`, 워커 스레드)와
  메시 빌드(`BuildMesh`, 메인 스레드)로 분리 → Terrain Forge가 멀티코어로 청크 굽기
* **WriteDensity 컬럼 캐시**: 해이트맵 조회를 복셀당 → 컬럼당으로 (resolution² 절감, 결과 동일)

### 편의 & 강력 기능

* **Shape 프리셋** 5종: Islands / Highlands / Dunes / Archipelago / Canyons 원클릭 적용
* **버텍스 바이옴 컬러**: High-Res Biome Map을 메시 버텍스 컬러로 자동 베이크
* **씬 자동 조립**: 베이크 후 `__WB_Terrain` 계층에 렌더러·메시 콜라이더·`TerrainChunkRenderer`
  등록까지 자동 — 플레이 모드에서 즉시 굴착 가능
* **지형 세이브 통합**: `WorldSaveService.SaveTerrain/LoadTerrain`(청크 델타 base64) +
  `TerrainDeformer.EditedChunks` 저널 + `WorldSaveManager` 자동 포함 옵션
* **단면 프리뷰**: 씬 카메라 전방의 지형 단면을 실시간 폴리라인으로 표시(파라미터 튜닝용)
* 성능 통계: 생성/베이크 시간·버텍스 수 리포트

### 테스트

* 병렬 vs 직렬 지오메트리 패리티, 버텍스 컬러 적용, 변형 저널+지형 세이브 왕복 테스트 추가

## 0.6.0 — Terrain Core

> "월드 빌더"라는 이름에 걸맞은 지형 엔진 코어. 네 개의 축(지형·바이옴·PCG·런타임 변형)을
> 기존 VoxelStore/청크 파이프라인 위에 얹었습니다.

### ① 절차적 지형 생성 + 침식

* `FbmNoise` — 시드 결정형 fBm(도메인 워프/리지드 포함, Unity.Mathematics simplex)
* `TerrainShapeParams` SO — 지형 형태 전체를 데이터로(버전 관리·해시 가능)
* `TerrainField` — 리전 하이트맵 생성 → **수적 드롭렛 + 열 침식**(`ErosionSimulator`) → 복셀 밀도 기록(기존 VoxelStore 그대로 사용)
* 침식 후 결과가 곧바로 스컬프트/익스포트/런타임과 동기화

### ① Surface Nets 메싱

* `VoxelWorldSampler` — 청크 경계를 넘는 삼선형 샘플러(심 자동 용접)
* `SurfaceNetsMesher` — 밀도 → 부드러운 메시(스커트 셀 포함, 소유 규칙 기반 쿼드, 노멀=밀도 그래디언트)

### ② 바이옴 고해상도화

* `BiomeClassifier` — 고도×온도×습도 Whittaker식 자동 분류
* `HighResBiomeMap` SO — 청크당 N×N 셀 저장, 이중선형 블렌딩 컬러/enum 조회

### ③ PCG 규칙 엔진

* `ScatterRuleSet` SO + `PcgScatterEngine` — 고도/경사/바이옴/노이즈 마스크 게이트, 셀 기반 Poisson-ish 밀도 배치(완전 결정적)
* `ScatterChunkBaker.BakePlacements` 공용 진입점으로 생태 결과를 청크 placements에 직접 베이크

### ④ 런타임 지형 변형

* `TerrainDeformer.Modify` — 구면 굴착/축조(delta±) → 영향 청크 리포트
* `TerrainChunkRenderer` 레지스트리 + `TerrainDeformer.Remesh` — 편집 즉시 Surface Nets 재메싱

### 에디터: Terrain Forge 도구

* 한 흐름 UI — ① Shape(생성+침식) → ② Mesh Bake(진행률/취소) → ④ Biomes 적용 → ⑤ Ecology 스캐터

### 테스트

* TerrainEngineTests — 노이즈 결정론, 밀도 부호 패턴, 침식 결정론·봉우리 감소, 메시 비어있음/평면 검증/결정론
* BiomeAndPcgTests — 분류 밴드, 스플랫 왕복, 경사 게이트, PCG 결정론

## 0.5.0

### 세이브 매니저

* `WorldSaveService` — persistentDataPath 기반 슬롯 저장/로드/삭제/목록(디렉터리 주입 가능, 테스트 친화적)
* `WorldSaveManager` — 프리팹 이름 바인딩 + 이벤트를 갖춘 씬 컴포넌트 파사드
* SaveServiceTests 5건 추가(경로 탈출 방지 포함)

### 게임 미니맵 UI

* `MinimapViewController` — 베이크된 미니맵 텍스처 위에 플레이어 마커 추적(월드 XZ→UV 매핑, MinimapBaker 좌표계와 일치)

### POI 상호작용 이벤트

* `PoiProximityTracker` — WorldDataSnapshot 기록 기반 반경 진입/이탈 이벤트(Entered/Exited), MessagePipe/VContainer 브리징은 게임 코드에서

### 시간·날씨 시스템

* `WorldClock` — day-night 사이클, Hour/Phase/Nightness, 선택적 태양 회전·강도, HourChanged/PhaseChanged 이벤트
* `SimpleWeatherController` — Clear/Overcast/Fog 프로파일 블렌딩(RenderSettings fog·ambient), 수동/주기 전환

### Water 쿼리 병렬 Job화

* `NativeWaterQuery` — WaterWorldRuntimeData의 NativeArray 미러 + IJobParallelFor 병렬 샘플링(Burst 정의 시 [BurstCompile])
* 직렬 `WaterQueryService`와 결과 패리티를 보장하는 테스트 추가

### Addressables 리전 로더

* `AddressablesRegionLoader`(WB_ADDRESSABLES define 시 컴파일) — com.unity.addressables 설치 시 자동 활성화되는 IRegionContentLoader 구현체

### 에디터 개선

* `StreamingSimulatorTool` — 씬 카메라 구동 RegionStreaming 미리보기(플레이 진입 없이 로드/언로드 검증)
* Import All Blender Chunks: 취소 가능한 진행률 바
* `ChunkRootEditor` — 청크 해시/매니페스트 표시 + 원클릭 재임포트

## 0.4.0

### WorldData 런타임 로더

* `WorldDataSnapshot`(Runtime) + `Tools > WorldBuilder > Export World Data Snapshot` 익스포터 — 에디터 WorldDataStore를 런타임 판독형 에셋으로 변환(결정적 정렬, 에디터 타입 제거)
* `WorldDataRuntimeLoader` — 시작 시 기록 로드, kind→프리팹 바인딩 인스턴스화, `RecordLoaded` 이벤트 제공
* EditMode 테스트 추가

### Unity 단독 스캐터 파이프라인

* `StrokePlacementBuilder` — 브러시 배치 수학을 정적 헬퍼로 추출(브러시/베이커 공유, 결정적 재현)
* `ScatterBakeTool` / `ScatterChunkBaker` — 기록된 스트로크를 기존 청크 매니페스트의 placements 문서로 굽기:
  - 청크 로컬 좌표 행렬 생성, `BlenderAssetRegistry` 역조회로 assetId 매핑
  - placements.json 병합 저장 → contentHash 갱신 → 표준 `ChunkImportPipeline.Import` 재임포트
  - 매니페스트 없는 청크는 스킵 리포트

### 미니맵 레이어 합성

* Minimap Baker 확장: **바이옴 레이어**(청크별 색), **수면 레이어**(레이캐스트+WaterQueryService 깊이 그라디언트), **청크 격자** 오버레이 픽셀 합성
* 레이어별 개별 PNG 동시 출력 옵션

### 월드 무결성 감사 도구

* `WorldAuditTool` — WorldDataStore 중복/NaN, 리전 카탈로그 누락·중복, 에셋 레지스트리 무결성,
  생성된 청크의 카탈로그 커버리지, 복셀 스토어 빈 버퍼를 원클릭 점검 + CSV 내보내기

### 런타임 엔티티 배치 완성

* `WorldEntityPlacementBridge` (Entities 어셈블리) — 이름→prefabId 바인딩으로 GameObject 배치를 DOTS 엔티티 스폰에 자동 미러링

### CI/테스트 자동화

* `Scripts/run-worldbuilder-tests.ps1` — Unity 배치 컴파일 체크(-CompileOnly) 및 `-testFilter WorldBuilder.Tests` 실행 스크립트

## 0.3.0

### Spline 배치 도구

* `SplinePlacementTool` — Unity Splines 경로 따라 프리팹 자동 생성(도로·강변·해안선)
* 간격/측면 오프셋/탄젠트 정렬/랜덤 요/스케일/표면 스냅 지원, 일괄 Undo, 루트 재생성·정리

### Prefab Brush 추가 확장

* **Brush Preset** 시스템: 프리팹 세트+배치 설정+마스크+그래프를 프리셋 에셋으로 저장/로드
* **Water Depth Mask** 노드: Water Runtime Data 기반 수심 범위 게이팅(건조 지역 필터 포함)
* ModifierContext에 `inWater`/`waterDepth` 추가(워터 데이터 미지정 시 0)

### 수중 시각화 도구

* `UnderwaterVisualizerTool` — 레이캐스트 지형 + WaterQueryService 샘플링으로 해저 수심 히트 그리드
* 커서 프롬프트(수심/수류 속도), 씬 WaterCurrentZone 화살표 강도 시각화

### Runtime Editing 고도화

* `RuntimePlacementService.ToJson` / `RestoreFromJson` — 세이브 데이터용 직렬화(알 수 없는 prefabId 스킵)
* `TryGetInstanceRecord` — 계층 탐색으로 소유 레코드 조회
* `RuntimeWorldEditor` Move 모드: 잡기→표면 따라 이동→놓기

### Blender 파이프라인

* **자동 임포트 Watcher**: SourceRoot의 `.chunk.json` 변경 감시 후 디바운스 재임포트 + 리전 카탈로그 갱신
* `Tools > WorldBuilder > Chunks > Auto Import` 토글(에디터 세션 유지)

### 성능 최적화

* `SpatialHash.Query` 버퍼 재사용 오버로드(Erase 핫패스 할당 제거)
* `ChunkStreamingService.SetFocusAsync` no-op 패스: 동일 포커스 반복 시 언로드/로드/옵저버 알림 생략

### 테스트/문서

* RuntimeEditingTests 4건 추가(JSON 왕복, 알 수 없는 id 스킵, 계층 소유 조회), StreamingTests no-op 1건
* 문서: BuiltInTools/RuntimeEditing/ModifierGraph/README 갱신

## 0.2.0

### Prefab Brush

* 새 Modifier Graph 노드 3종: `RandomMaskNode`, `CellMaskNode`, `BrushEdgeMaskNode`
* 마스크 게이팅: Scale 채널 평가 결과가 0인 배치는 프리뷰/배치에서 자동 스킵
* 드래그 페인팅(`Paint On Drag` + `Drag Spacing`) 추가
* 프리팹 프리뷰용 MeshFilter 캐싱으로 Scene GUI 성능 개선

### 새 에디터 도구

* **Minimap Baker** — 정사영 탑다운 미니맵 PNG 베이크(투명 배경, 레이어 마스크 지원)
* **POI Placer** — POI/루트 컨테이너 배치 및 WorldDataStore 등록, Remove 모드

### Runtime Editing (기반)

* `WorldBuilder.Runtime.Editing.RuntimePlacementService` — 런타임 배치 장부/이벤트
* `RuntimeWorldEditor` — 고스트 프리뷰 런타임 에디터 컴포넌트
* `WorldEntityRuntimeEditing` — 엔티티 런타임 스폰 파사드
* EditMode 테스트 추가 (`RuntimeEditingTests`)

### 시각화

* Chunk Grid Visualizer: 청크 좌표 라벨, 리전 경계 표시, 커서 청크 하이라이트 옵션

### Blender 파이프라인

* `Tools > WorldBuilder > Chunks > Validate All Blender Chunks` — 임포트 없는 전체 검증(dry-run)
* 지오메트리가 청크 크기를 초과하면 경고(`WB_IMPORT_GEOMETRY_BOUNDS`)
* 검증 로직을 `ChunkImportPipeline.Validate`로 분리(Import와 동일 규칙 공유)

### 기타

* 패키지를 임베디드 형태로 개발할 수 있도록 정리
* 문서: `Documentation/KR/RuntimeEditing.md` 신규, BuiltInTools/ModifierGraph/README 갱신

## 0.1.0

* 초기 릴리스

# Changelog

## 0.16.0 — Gameplay Interaction Batch

### 낚시

* `FishingSpot` — 베이크된 물 쿼리로 수역 검증, 깊이 밴드 게이트가 있는 가중 어종 테이블,
  입질 지연/릴 윈도우 파라미터
* `FishingSession` — 결정론 상태 머신(대기→입질→릴 성공/도주). 입질 시점에 어종 선롤 →
  릴 윈도우 안의 입력만 보상. 입력 UI는 게임 코드에서 Tick/TryReel로 구동

### 수중 생존

* `WaterBreather` — 도메인 기반 공기 게이지(잠수 시 감소, 수면 회춘), 익사 시작/종료/틱 이벤트
  (HP는 게임 코드 소유), 공기 부족 시 `WaterDrifter` 부양 스위치 온 패닉 스윔 어시스트

### 저장 UI

* **uGUI 세이브 메뉴 샘플** — `SaveMenuUIBuilder.Build(parent, service)`: 슬롯 목록+저장/불러오기/
  삭제 버튼을 코드로 조립(`WB_UGUI` 버전 디파인 가드 — uGUI 없는 프로젝트 컴파일 유지)

### 스트리밍 파이프라인

* **Addressables 리전 자동화** — `Assets/WorldBuilderGenerated/Regions/R_*` 폴더를 리전당 그룹으로
  묶어 주소 부여 + 콘텐츠 빌드 메뉴 두 개(`WB_ADDRESSABLES_EDITOR` 가드, 빌드 API는 리플렉션으로
  버전 무관 동작)

### 테스트

* 입질→릴 포획, 미릴 도주, 심수 종 게이트, 공기 감소/회춘, 아가미 무익사 테스트 추가

## 0.15.0 — Living World

### 낮밤·날씨

* `DayNightAtmosphere` — WorldClock의 고도 곡선을 따라 태양 색 그라디언트(일출~정오~일몰) 적용,
  밤에는 포그/앰비언트를 한랭 틴트로 승수(다른 시스템이 쓴 절대색을 보존하며 합성),
  SeasonPalette로 계절 온기/한기 편차까지 반영
* SimpleWeatherController에 **Rain 상태** 추가(전용 프로필, 자동 전환 후보에 합류)

### 강수 → 물 루프

* `PrecipitationFx` — Rain/Overcast에서 파티클 강수(프리팹 또는 절차적 폴백 빗줄기), 지형 젖음
  글로벌 채널 `_WB_Wetness` 구동, `WaterLevelDriver`에 폭풍 강도 자동 주입 —
  **비 → 젖은 지형 + 강 불어남**이 하나의 컴포넌트로 닫힘
* TerrainSplat 셰이더가 `_WB_Wetness`를 소비해 알베도 감쇠 + 스무스니스 상승

### 성장·채집

* `GrowableResource` — 단계별 비주얼 스왑, 시간 기반 성장(Growth01 노출, 카메라 가시성 게이트)
* `HarvestableNode` — 수확 가능 성숙 판정, 아이템 롤(min/max), 수확 후 0단계 리스폰 또는 파괴,
  형제 GrowableResource 지연 바인딩

### 저장

* `AutoSaveService` — 링 회전 autosave_NN 슬롯(보관 수 상한, 종료 시 저장 옵션) on SaveSlotMenuService

### 테스트

* 성장 단계 진행, 성숙 게이트+롤 범위+리스폰, 파괴 모드 테스트 3건 추가

## 0.14.0 — Visuals, Tools & Pipeline Batch

### 렌더링

* **URP Volume FX** — EnvironmentFxRig에 도메인별 `VolumeProfile` 스왑 추가
  (`WB_CORE_RP` 버전 디파인 가드, Core RP 어셈블리 참조)
* **트라이플래너 스플랫** — TerrainSplat 셰이더가 경사면에서 월드스페이스 삼면 매핑으로
  전환(슬라이더 조절) — 절벽 텍스처 스트레칭 제거
* **물 상호작용 이펙트** — `WaterSplashFx`: 진입/이탈 시 파티클 스플래시 + 절차적 링 립플
* **동굴 발광 자동화** — *WorldBuilder/Caves/Place Glow Lights*: 공동 중앙에 웜/쿨 PointLight 자동 배치

### 저작 도구

* **월드맵 생성기** — `WorldMapBaker` + 수심·바이옴·동굴 오버레이 합성 전략지도 PNG 익스포트 메뉴
* **월드 시드 공유 포맷** — `WorldSeedCodec`(Export/Import/Fingerprint) + wbseed 파일 메뉴:
  시드 파일만 주고받아 동일 월드 재현
* **생태 브러시** — `EcologyBrushTool`: 씬에서 Ctrl+드래그로 규칙 게이트를 통과한 배치를 직접 칠하고 청크에 베이크
* **세이브 UI 퍼사드** — `SaveSlotMenuService`: UI 비의존 세이브 메뉴 로직(목록/저장/복원/삭제)

### 파이프라인

* **성능 벤치마크** — *WorldBuilder/Audit/Run Performance Benchmark*: 메싱/수질 쿼리(직렬 vs Burst)/
  카빙 처리량 리포트
* **편집 델타 포맷** — `TerrainEditCodec`: 명령 패킷 JSON + FNV 체크섬 + `Replay`
  (멀티플레이 지형 동기화 기반)

### 문서

* ScriptingAPI.md에 0.9~0.13 신규 API 섹션 추가

### 테스트

* 편집 패킷 왕복·체크섬 변조 거부·재생 카빙 테스트 추가

## 0.13.0 — Simulation Batch

### 물리/시뮬레이션

* **Rigidbody 부력** — `WaterDrifter`가 이제 리지드바디를 힘으로 조종
  (`ComputeSteeringForce`: 목표 유속까지의 스티어링 포스). 충돌·적재·컨트레인트 유지,
  트랜스폰모드는 기존 대로 선택 가능
* **런타임 강 침식** — `RiverbedFlowSim`이 베이크된 강 세그먼트를 따라 예산화된 스탬프로
  급류 지역은 깎고 유속 낮은 구간은 퇴적. `WaterLevelDriver` 날씨 배수가 침식률에 직결
* **동굴 붕괴 감지** — `CaveStabilityAnalyzer.FindDetachedSolid`: 월드 바닥 행에서 고형 복셀을
  플러드 필해 도달 불가능한 클러스터(공중 부양 지붕)를 탐지. 노드 예산 캡 + 샘플 위치 반환.
  `CollapseWatcher`가 변형 이벤트를 스로틀 감시 → 경고/마커 프리팹/이벤트
* **어군 스폰** — `IVolumeQuery.TryCavity`(공동 수직 중앙+클리어런스)와
  `VoxelVolumeScatter.GenerateMidWater`, 팩토리 `FishSchools` 프리셋

### 테스트

* 스티어링 포스 수렴, 강 침식 밀도 총합 감소, 지지/무지지 슬래브 연결성, 공동 중앙 어군 배치 등 6건 추가

## 0.12.2 — Cross-Tool Entrances & Seasonal Bakes

### 블렌더 ↔ Unity 동굴 입구 정렬

* Blender Cave Network Builder에 **입구 마커 익스포트**(기본 on) — 터널 최고점마다
  `CaveEntrance_NN` Empty 생성
* `CaveField.CarveEntranceAt` 공개 API — 지정 XZ 단일 컬럼 관통(공기 탐색 실패 시 0 반환)
* `WorldBuilder/Caves/Carve Entrances At Marker Objects` 메뉴 — 씬의 마커를 읽어 로컬 하이트맵으로
  보호 깊이를 재계산한 뒤 샤프트 카빙 + 주변 청크 즉시 리메시

### Terrain Forge

* **Season Palette 연동** — 팔레트 SO + 베이크 시즌 슬라이더(-1 off / 0 봄~3 겨울).
  버텍스 컬러를 계절색으로 70% 블렌딩하되 기존 이중선형 경계 부드러움 유지

### EnvironmentFxRig

* 도메인별 **AudioMixerSnapshot 자동 전환** — 수중 머플/동굴 리버브 등을 DomainLook에 스냅샷으로
  지정하면 도메인 변경 시 크로스페이드

### 테스트

* 단일 컬럼 입구 관통(성공/무공기 0 반환), 블렌더 마커 개수·이름 검증으로 스모크 강화

## 0.12.1 — Audit & Tool Wiring

### 검증(Audit)

* `WorldAuditRules` — 복셀 스토어 직접 검사 3종:
  **고아 청크**(축 방향 이웃 전무+고형 비율 상한), **밀도 이상**(NaN/범위 밖),
  **경계 불연속**(인접 청크 접촉면 밀도 불일치 → 세이브/수동 편집 후 잠재적 심 감지)
* `WorldBuilder/Audit/Run Terrain Checks` 메뉴 — 콘솔 리포트 출력

### Terrain Forge 연동 완성

* 수중 게이트가 실제로 작동 — `waterData` 필드 추가 시 `VoxelTerrainQuery`가
  `IWaterAwareTerrainQuery`로 동작해 Coral/Kelp 규칙의 수심·유속 게이트가 적용됨
* **Scatter Cave Interior** 버튼 — 카브 파라미터의 Y 범위를 부피로 하여
  `VoxelVolumeScatter`로 동굴 내부(광맥/이끼) 배치를 청크에 굽기
* `WorldBuilder/PCG/Create Rule Set/*` 메뉴 — 생태 프리셋 에셋 즉석 생성

### 테스트

* 고아 청크 플래그/해제, NaN·범위 이상 탐지, 경계 불일치 보고 테스트 3건 추가

## 0.12.0 — Authoring Batch 2

### PCG

* **볼륨 스캐터 엔진** — `VoxelVolumeScatter` + `IVolumeQuery`/`VoxelVolumeQuery`:
  지표가 아닌 동굴 내부 바닥을 실제로 걸어 내려가 배치하는 절차적 스폰(광맥·형광 이끼· stalagmite).
  바이옴 게이트·바닥 법선 경사 게이트·성장단계 프리팹 지원
* `ScatterRuleSetFactory` — CoralReef / KelpForest / CaveInterior 원클릭 규칙 세트
  (수심 밴드·유속 상한이 올바르게 세팅된 상태로 생성)
* 스캐터 규칙에 **성장 단계(growth stages)** 추가 — 리스폰 시 새싹→성체 프리팹 순환

### 저작 도구

* **메시→복셀 카빙** — `MeshCarver.CarveAlongSurface` + `WorldBuilder/Caves/Carve Store With
  Selected Mesh` 메뉴: Blender Cave Network Builder 산출물을 선택해 복셀 스토어에 그대로 파기,
  등록된 청크는 즉시 리메시
* `SeasonPalette` — 바이옴×계절 컬러 팔레트 SO + 연속 계절 블렌딩 샘플러(순환 지원)
* `MinimapDepthBaker` — 복셀에서 직접 만드는 수심 그라디언트 맵 + 밀폐 동굴 오버레이 레이어
* `CreatureWaypointPath` — Catmull-Rom 폐쇄 순찰 경로(거리 기반 평가, 정지 시간, 속도 배수) +
  씬 뷰 시각화 에디터
* `StreamingBudgetPreset`/`StreamingBudgetDriver` — Handheld/Desktop/Server 예산 프리셋을
  ChunkStreamingService에 주기 적용

### 렌더링/분위기

* **`WaterSurface` URP 셰이더** — 3방향 사인파 정점 변위 + 해석적 노멀, 프레넬 하이라이트,
  파고 기반 폼. 깊이 색 혼합 프리미티브 포함
* `EnvironmentFxRig` — 환경 도메인(OpenAir/Underwater/Underground/FloodedCave)에 따라 포그·
  앰비언트·일광 강도를 부드럽게 전환(RP 비의존)

### 테스트

* 볼륨 스캐터 바닥 배치·결정론, 팩토리 게이트, 메시 튜브 관통, 계절 블렌딩/랩, 경로 길이·연속성·
  랩어라운드 테스트 6건 추가

## 0.11.0 — World Flow & Authoring Batch 1

### 지형 저작

* `RiverbedCarver` — 강 중심선 폴리라인을 따라 복셀 하상 채널 자동 절삭(TerrainDeformer 스윕 재사용)
* `CaveField.CarveEntrances` — 카빙된 동굴 공기 주머니를 향해 표면에서 보행 가능한 샤프트를
  결정론적으로 관통(시도 상한 내 배치). 밖에서 걸어 들어가는 동굴 완성

### 물 흐름 확장

* **지하수대** — `GroundwaterService`가 waterTableY 아래를 정수로 처리.
  `EnvironmentClassifier`와 조합 시 테이블 아래 밀폐 공간이 자동으로 FloodedCave.
  `CaveShapeParams.waterTableY` 필드 추가
* **날씨→수위 연동** — `WaterLevelDriver`: 0..1 강도(가뭄~홍수) → 해수면 오프셋 + 유속 배수.
  `WaterWorldRuntimeData`에 런타임 전용 오프셋/배수 적용 API(베이크 데이터 불변), 쿼리 서비스가
  유효 해수면/유속 사용

### PCG 수중 게이트

* `ScatterRuleSet.Rule`에 수심 밴드(min/maxDepth)·최대 유속 게이트 추가 +
  `IWaterAwareTerrainQuery` 선택 인터페이스 — 산호/해초/조개 규칙이 실제 수심과 급류를 존중

### 저장

* **통합 월드 세이브 v2** — `SaveSnapshot/LoadSnapshot`: 배치 JSON + 지형 델타 + extras를 한 번에
  직렬화/복원(기존 슬롯과 하위 호환)

### 테스트

* 지하수 침수 분류, 수위 구동, 수중 게이트 필터, 스냅샷 왕복 테스트 4건 추가

## 0.10.0 — Water Flow

### 해류·물 흐름 시스템

* **바다 기본 해류** — `OceanWaterBody.BaseFlowDirection/BaseFlowSpeed` 추가. 수면 아래 모든
  바다 샘플에 전역 흐름이 실린다 (`WaterSample.FlowDirection/FlowSpeed`)
* **`WaterCurrentZone`이 드디어 파이프라인에 합류** — 기존엔 베이크되지 않던 컴포넌트를
  `CurrentZoneData`로 베이크. 존은 물을 만들지 않고 "이긴 물의 흐름을 교체"한다
  → 폭포 하강 기류, 소용돌이, 강 하구 역류 등 바다/호수/강 어디든 얹을 수 있음
  (우선순위 겹침 처리 포함, 셀 인덱스 + 결정론 해시에 완전 반영)
* 시리얼 `WaterQueryService`와 Burst `NativeWaterQuery`가 동일 의미론으로 확장 —
  기존 패리티 테스트가 양쪽 일치를 자동 검증

### 표류(Drift) 시스템

* `WaterDrift.Integrate` — 순수 상태 적분 수학: 흐름 가속(항력은 편차에만 작용해 강류 속도가
  항상 우세), 부력 스프링(수면 부양선 수렴), 침하 모드, 공기 중 중력
* `WaterDrifter` MonoBehaviour + 정적 레지스트리 — 서비스 주입형(FixedUpdate), 부양 시 흐름
  방향으로 부드러운 선회. 배·파편·플레이어 표류에 즉시 사용 가능

### 테스트

* 해류 전역 적용, 존 오버라이드(경계 밖/공기 무시), 다중 존 우선순위, 흐름 수렴·부양 수렴·침하 테스트 6건 추가

## 0.9.1 — Cave Authoring Bridge

### Blender 애드온

* **Cave Network Builder** (`cave_generator.py`) — Unity `CaveField`의 프리셋(Limestone/LavaTubes/
  FloodedGrotto/AbyssalNetwork)과 대응하는 절차적 동굴 터널 생성기. 병렬 운반(parallel transport)
  프레임 기반 폐쇄 튜브 메시 + 케버른 룸 불룸 + 수직 스쿼시
* 생성된 정점에 `WB_BIOME_CAVE` 가중치 속성 자동 부여 — 청크 익스포트 시 Unity에서 Cave 바이옴으로 분류
* WorldBuilder 사이드바에 KO/EN 패널 추가, 시드 결정론 보장(동일 시드 → 동일 지오메트리)
* 스모크 테스트 `blender_cave_generator_smoke.py` 추가(4프리셋 생성·결정론·속성·경계 검증)

### Unity 런타임/툴

* `TerrainDeformer.Drill` — 두 점 사이를 구형 커터로 스윕하는 터널 굴착 API(저널/이벤트 연동,
  간격 없는 스윕을 위해 반경 적응 스텝). 플레이어 채굴·웜 AI 굴로에 사용
* `CaveAmbientTint` — 버텍스 단위 동굴 암부 셰이딩. Terrain Forge에 "Darken Cave Vertices"
  토글 추가(병렬 베이크 패스 안전, 스레드별 샘플러)
* `EnvironmentClassifier.ClassifyBatch` — 다수 위치 일괄 환경 판정(스폰 시스템용)

### 테스트

* 드릴 연속 터널·원거리 보존, 앰비언트 틴트(커버만 암화·심도 그라데이션), 배치 분류 일치 테스트 추가

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

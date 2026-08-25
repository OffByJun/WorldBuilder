# Built-in Tools

이 문서는 WorldBuilder에 기본 포함되어 있는 Tool을 설명합니다.

모든 Tool은 WorldBuilder Tool System을 기반으로 동작하며,
Toolbar에서 선택하여 사용할 수 있습니다.

---

# Mesh Edit Tool

## 목적

메시를 직접 수정하기 위한 편집 도구입니다.

## 주요 기능

- Mesh 선택
- Geometry 편집
- Scene View 상호작용

---

# Terrain Paint Tool

## 목적

Terrain 또는 월드 표면을 브러시 기반으로 수정합니다.

## 주요 기능

- 브러시 페인팅
- 실시간 미리보기
- 반복 편집

---

# Voxel Paint Tool

## 목적

Voxel 기반 데이터를 편집합니다.

## 주요 기능

- Voxel 추가
- Voxel 제거
- 브러시 크기 조절

---

# Prefab Brush Tool

## 목적

Prefab을 빠르게 배치하는 브러시입니다.

## 주요 기능

- 연속 배치
- 랜덤 배치
- 브러시 방식 배치

---

# Spawn Edit Tool

## 목적

Spawn 데이터를 생성하고 수정합니다.

---

# Creature Spawn Zone Tool

생물 스폰 영역을 생성하고 관리합니다.

---

# Event Trigger Zone Tool

게임 이벤트가 발생하는 Trigger Zone을 생성합니다.

---

# Temperature Zone Tool

온도 구역을 정의합니다.

---

# Pressure Zone Tool

압력 구역을 정의합니다.

---

# Toxic Zone Tool

독성 구역을 생성합니다.

---

# Visibility Zone Tool

가시성(Visibility) 영역을 관리합니다.

---

# Water Current Tool

물의 흐름(Current)을 설정합니다.

---

# Air Pocket Tool

공기 주머니(Air Pocket)를 생성하여 플레이어가 산소를 보충할 수 있는 영역을 정의합니다.

---

# Wreckage Tool

잔해(Wreckage) 데이터를 생성하고 관리합니다.

---

# Bin Importer Tool

외부 Binary 데이터를 가져옵니다.

---

# Export Tool

편집한 데이터를 Runtime에서 사용할 수 있는 형태로 Export합니다.

---

# Material Batch Tool

여러 Material을 일괄 처리합니다.

---

# Height Biome Mapper

높이를 기준으로 Biome을 매핑합니다.

---

# Biome Setter Tool

Biome 데이터를 편집합니다.

---

# Bioluminescence Tool

발광(Bioluminescence) 데이터를 설정합니다.

---

# Environment Overlay Tool

환경 정보를 Overlay 형태로 시각화합니다.

---

# Chunk Grid Visualizer

Chunk Grid를 Scene View에 표시합니다.

---

# Depth Layer Visualizer

깊이 레이어를 시각화합니다.

---

# Spawn Heatmap Tool

Spawn 밀도를 Heatmap 형태로 표시합니다.

---

# Path Tool

경로(Path)를 생성하고 편집합니다.

---

# Undo History Tool

WorldBuilder 내부의 Undo 이력을 표시하고 관리합니다.

---

# 확장 도구 (생산성 / 시각화 / 자동화)

아래 도구들은 모두 `IWorldBuilderTool`을 구현하며 `WorldBuilderBootstrap`에 등록됩니다.
UI는 UI Toolkit으로 구성되고, 파괴적 작업은 Undo를 지원합니다. 공통 씬 수집은 `SceneObjectCollector`,
도구별 배치/임포트/계산 로직은 SRP에 따라 `*Service` 클래스로 분리됩니다.

---

# Scene Bookmark Tool

씬 카메라 시점(위치·회전)을 북마크로 저장/복원합니다.

## 주요 기능

- 이름 입력 + Save → 현재 SceneView 카메라 저장
- 목록 항목 클릭 → 해당 시점으로 이동, Delete로 삭제
- EditorPrefs(JSON) 영속화 (`WB_SceneBookmarks`)

---

# Layer Batch Tool

대상 오브젝트의 Layer를 일괄 변경합니다.

## 주요 기능

- 소스/타겟 Layer, 범위(씬 전체/Selection), 자식 포함 여부
- `LayerBatchService` 처리, Undo 지원

---

# Scene Search Tool

씬 오브젝트를 실시간 필터링합니다.

## 주요 기능

- 필터 타입: Name / Component / Layer / Tag
- 결과 클릭 → 선택 + Ping, OnSceneGUI에서 와이어 큐브 강조
- Component 필터는 `GetType().Name` 부분일치(어셈블리 리플렉션 미사용)

---

# Prefab Batch Tool

프리팹 인스턴스의 오버라이드를 일괄 처리합니다.

## 주요 기능

- 탭: Apply Overrides / Revert Overrides, 범위(씬 전체/Selection)
- `PrefabBatchService` 처리, Undo 지원

---

# Draw Call Heatmap Tool

렌더러의 드로우콜(머티리얼 수 기준) 부하를 색으로 표시합니다.

## 주요 기능

- Refresh로 렌더러 수집, 낮음/높음 임계값 슬라이더
- 초록/노랑/빨강 오버레이 + 드로우콜 수 라벨

---

# Collider Visualizer Tool

씬의 모든 콜라이더를 타입별 색상·투명도로 와이어 표시합니다.

## 주요 기능

- Box/Sphere/Capsule/Mesh 색상 지정, Show All 토글

---

# Light Range Tool

라이트 범위를 시각화합니다.

## 주요 기능

- Point: 디스크, Spot: 원뿔 호, Directional: 방향 화살표
- 타입별 색상·투명도

---

# Scene Snapshot Tool

씬 오브젝트의 Transform·활성 상태를 스냅샷으로 저장/복원합니다.

## 주요 기능

- Save / 항목별 Restore·Delete, Undo 지원
- EditorPrefs(JSON) 영속화 (`WB_SceneSnapshots`)

---

# Placement Rule Tool

배치 규칙(소스/이웃/최소·최대 거리)을 정의하고 위반 오브젝트를 검출합니다.

## 주요 기능

- 규칙 리스트 + Validate
- 위반 오브젝트는 빨간 와이어 큐브로 표시

---

# Mesh Optimizer Tool

메시를 최적화합니다.

## 주요 기능

- 중복 버텍스 제거(weld) / 미사용 버텍스 제거 / UV 정리
- `MeshUtility.Optimize` 호출, 버텍스 수 before/after 표시, Undo 지원
- 로직: `MeshOptimizerService`

---

# FBX Import Tool

폴더 내 모든 FBX에 임포트 설정을 일괄 적용합니다.

## 주요 기능

- 프리셋(스케일/노멀/탄젠트/콜라이더/노멀 모드) 저장·삭제, 대상 폴더 지정
- `FBXImportService`에서 `ModelImporter` 설정 후 재임포트
- 프리셋 영속화 (`WB_FBXImportPresets`)

---

# Texture Import Tool

폴더 내 모든 텍스처에 임포트 설정을 일괄 적용합니다.

## 주요 기능

- 프리셋(압축/MipMap/MaxSize/포맷/노멀맵) 저장·삭제, 대상 폴더 지정
- `TextureImportService`에서 `TextureImporter` 설정 후 재임포트
- 프리셋 영속화 (`WB_TextureImportPresets`)

---

# Shader Live Edit Tool

머티리얼의 셰이더 프로퍼티를 자동 파싱해 실시간 편집합니다.

## 주요 기능

- 타입별 필드: Float/Range → Slider, Color → ColorField, Vector → Vector4Field, Texture → ObjectField
- 변경 즉시 머티리얼 반영, Reset으로 원본 복원, Undo 지원

---

# Material Compare Tool

두 머티리얼의 프로퍼티 값을 비교합니다.

## 주요 기능

- 결과 리스트: 이름 / 좌측 / 우측, 다른 값은 경고색(#FF8C00) 강조
- 비교 로직: `MaterialCompareService`

---

# Texture Atlas Tool

여러 텍스처를 아틀라스로 패킹합니다.

## 주요 기능

- 텍스처 리스트, 크기(512~4096), 출력 경로
- `Texture2D.PackTextures` → PNG 저장 → Refresh, UV Rect 표시
- 로직: `TextureAtlasService` (원본 텍스처 Read/Write 필요)

---

# UV Visualizer Tool

메시 UV를 월드 공간에 시각화합니다.

## 주요 기능

- UV 채널(UV0~UV3), 색상 지정
- UV 엣지 라인 표시, 경계 엣지(시임)는 강조색

---

# Audio Visualizer Tool

씬의 AudioSource를 시각화합니다.

## 주요 기능

- 3D(spatialBlend>0): min(불투명)/max(반투명) 거리 디스크
- 2D: "2D" 라벨, 2D/3D 색상·투명도 지정

---

# Audio Mixer Preset Tool

오디오 믹서 파라미터를 프리셋으로 저장/적용합니다.

## 주요 기능

- `AudioMixerPreset`(ScriptableObject)의 파라미터 목록 기준
- Save → 현재 믹서 값 읽기, Apply → 적용

---

# Unused Asset Tool

폴더 내 미사용 에셋을 검색합니다.

## 주요 기능

- Scan → 현재 활성 씬의 의존성과 비교(`AssetDatabase.GetDependencies`)
- 항목별 Ping/Delete, 확인 다이얼로그 포함 Delete All
- 로직: `UnusedAssetService`
- 주의: 다른 씬/동적 로드 전용 에셋도 미사용으로 잡힐 수 있음

---

# Asset Report Tool

씬의 텍스처·메시 사용 현황을 집계합니다.

## 주요 기능

- 텍스처 탭: 이름/해상도/포맷/메모리(MB)
- 메시 탭: 이름/버텍스/트라이앵글/메모리(MB), 탭별 총합
- CSV Export, 로직: `AssetReportService`

---

# Scene Changes Tool

마지막 baseline과 현재 씬을 비교합니다.

## 주요 기능

- Save Baseline → Transform 스냅샷(EditorPrefs JSON, `WB_SceneChangeBaseline`)
- Scan Changes → 추가(초록)/삭제(빨강)/이동(노랑) 구분, 항목 클릭 시 선택
- 비교 기준: 오브젝트 이름(동명 오브젝트 구분 불가)

---

# Object Owner Tool

오브젝트에 작업자 정보를 태깅합니다.

## 주요 기능

- `OwnerTag`(MonoBehaviour, Runtime) 추가/업데이트, Undo 지원
- OnSceneGUI에서 작업자 색 와이어 큐브 + 이름 라벨

---

# Rigidbody Batch Tool

Rigidbody 설정을 일괄 적용합니다.

## 주요 기능

- mass / linearDamping / angularDamping / useGravity / isKinematic / constraints
- 항목별 적용 토글로 변경할 값만 선택, 범위(씬 전체/Selection)
- `RigidbodyBatchService`, Undo 지원

---

# Collider Fitter Tool

메시 바운즈에 맞춰 콜라이더를 자동 생성합니다.

## 주요 기능

- 타입: Box/Sphere/Capsule, 기존 콜라이더 교체 토글
- OnSceneGUI 미리보기(와이어), `ColliderFitterService`, Undo 지원

---

# LOD Generator Tool

LOD 메시와 LODGroup을 생성합니다.

## 주요 기능

- LOD 단계(2~4), 단계별 폴리곤 비율
- 정점 클러스터링(`LODMeshSimplifier`)으로 감소 메시 생성 → 자식 LOD 오브젝트 + LODGroup 구성
- `Undo.RegisterCreatedObjectUndo` 지원

---

# Lighting Preset Tool

조명 환경을 프리셋으로 저장/적용합니다.

## 주요 기능

- `LightingPreset`(ScriptableObject, Runtime): 환경광·안개 설정
- Save → 현재 `RenderSettings` 캡처, Apply → 적용, 항목별 Delete

---

# Static Flag Tool

Static Editor Flags를 일괄 변경합니다.

## 주요 기능

- 범위(씬 전체/Selection), 자식 포함, `StaticEditorFlags`(EnumFlags)
- `GameObjectUtility.SetStaticEditorFlags`, Set/Clear, Undo 지원

---

# Object Snap Tool

선택 오브젝트를 그리드/표면에 스냅합니다.

## 주요 기능

- Grid: 그리드 크기로 반올림 / Surface: 아래로 Raycast + 오프셋
- Enable 시 드래그 후(MouseUp) 스냅 적용, Undo 지원

---

# Transform Batch Tool

선택 오브젝트의 Transform을 일괄 조정합니다.

## 주요 기능

- 정렬(축, Min/Center/Max) / 분산(축, 간격) / 초기화(P·R·S 개별)
- `TransformBatchService`, Undo 지원

---

# Terrain Sculpt Tool

복셀 밀도를 브러시로 조각합니다.

## 주요 기능

- 모드: Add / Subtract / Smooth, 반경·강도
- 기존 `IVoxelStore` 인터페이스로만 통신(DIP/ISP)
- OnSceneGUI 브러시 미리보기, Undo 지원

---

# Minimap Baker Tool

씬을 정사영 탑다운 카메라로 캡처해 미니맵 PNG로 굽습니다.

## 주요 기능

- 씬 뷰 피벗 자동 중심 또는 수동 Center 지정
- World Extent(XZ 범위), 해상도(64~8192), Far Plane 설정
- 레이어 마스크 필터링, 투명 배경(알파) PNG 출력
- Scene View에 베이크 영역 와이어 프리뷰 표시

---

# POI Placer Tool

관심 지점(POI)과 루트 컨테이너를 클릭으로 배치합니다.

## 주요 기능

- Marker Type: POI / Loot Container, 표시 이름 지정
- 배치 시 `WorldDataStore`에 `POIEntry`/`LootContainerEntry` 자동 등록
- Remove 모드: 반경 3m 내 가장 가까운 마커 제거
- 배치된 마커는 월드 데이터 브라우저에서 탐색 가능

---

# Spline Placement Tool (0.3.0)

Unity Splines 경로를 따라 프리팹을 자동 생성합니다. 도로·강변·해안선 장식에 유용합니다.

## 주요 기능

- SplineContainer 지정, 간격(Spacing) 기반 슬롯 산출
- 측면 랜덤 오프셋, 탄젠트 정렬, 랜덤 요, 스케일 범위
- 표면 스냅(Raycast + 노멀 정렬), Y 오프셋
- 단일 루트 아래 생성 → 일괄 Undo, Clear로 정리
- Scene View에 스플라인 경로 프리뷰 표시

---

# Underwater Visualizer Tool (0.3.0)

베이크된 수면 데이터(Water Runtime Data)를 편집 모드에서 시각화합니다.

## 주요 기능

- 지형 레이캐스트 + `WaterQueryService` 샘플링으로 해저 수심 히트 그리드(얕음→깊음 그라디언트)
- 커서 프롬프트: 해당 지점 수심·수류 속도
- 씬의 WaterCurrentZone 화살표를 강도 비례 크기/알파로 표시
- 셀 크기·뷰 반경·최대 수심 스케일·레이어 마스크 조절

---

# Scatter Bake Tool (0.4.0)

Prefab Brush로 기록한 스트로크를 Blender 없이 청크에 영구 배치합니다.

## 주요 기능

- 기록된 스트로크를 결정적으로 재현해 청크별로 그룹핑
- `BlenderAssetRegistry` 역조회로 프리팹→assetId 매핑
- placements.json 병합 저장 후 contentHash 갱신 → 표준 임포트 파이프라인 재임포트
- 매니페스트가 없는 청크는 스킵 사유와 함께 리포트

---

# World Audit Tool (0.4.0)

월드 데이터 전반을 원클릭 교차 점검합니다.

## 주요 기능

- WorldDataStore: 중복 id, NaN 위치, 빈 표시 이름
- DirectRegionCatalog: null 프리팹, 중복 좌표
- BlenderAssetRegistry: 빈/중복 assetId, 누락 프리팹
- 생성된 청크 ↔ 리전 카탈로그 커버리지 검사
- VoxelStore 빈 밀도 버퍼 점검
- 결과 리스트 표시 + CSV 내보내기

---

# Streaming Simulator Tool (0.5.0)

플레이 모드 진입 없이 RegionStreaming을 미리 봅니다.

## 주요 기능

- 씬 뷰 카메라(또는 수동 Refocus)를 포커스로 ChunkStreamingService 구동
- 리전 반경 슬라이더, DirectReferenceRegionLoader로 실제 청크 프리팹 인스턴스화
- 생성 인스턴스는 `__WB_StreamingPreview` 루트에 수집, Unload All로 정리

---

# Terrain Forge Tool (0.6.0)

절차적 지형 코어 워크벤치입니다.

## 주요 기능

- **Shape**: 시드 기반 fBm(도메인 워프·리지드·테라스·아일랜드 감쇠)으로 리전 하이트맵 생성
- **Erode**: 수적 드롭렛 + 열 침식 시뮬레이션(결정적)
- **Write**: 침식된 하이트맵을 VoxelStore 밀도로 변환 — 스컬프트/익스포트/런타임과 동일 데이터
- **Bake Meshes**: Surface Nets 메셔로 청크 심이 용접된 메시 애셋 출력
- **Biomes**: 고도×온도×습도 Whittaker 분류 → 고해상도 바이옴 스플랫
- **Ecology**: 규칙(고도/경사/바이옴/노이즈) 기반 PCG 스캐터 → Scatter Bake 파이프라인으로 청크 베이크

### 런타임 지형 변형

* `TerrainDeformer.Modify` — 구면 굴착/축조 후 영향 청크 자동 재메싱(`TerrainChunkRenderer` 레지스트리)
* Valheim식 파괴/건설 게임플레이의 기반

### 비주얼 파이프라인 (0.8.0)

* **스플랫맵**: 바이옴→4레이어 매핑으로 청크 컨트롤 텍스처 생성, `WorldBuilder/TerrainSplat` URP 셰이더가 4종 텍스처를 블렌딩. 버텍스 컬러 G 채널이 낮은 곳(침식 지역)은 자동으로 암석 노출
* **LOD 체인**: LOD1/LOD2 메시 자동 생성 + `LODGroup` 구성, 버텍스 컬러 보존
* **침식 맵**: 침식(R)/퇴적(G) 강도 PNG 출력 — 스플랫 가중치나 생태 규칙 입력으로 재사용 가능

---

# Summary

현재 WorldBuilder에는 다음 카테고리의 Tool이 포함되어 있습니다.

| Category | Tools |
|----------|------|
| Mesh | Mesh Edit |
| Terrain | Terrain Paint, Voxel Paint |
| Prefab | Prefab Brush |
| Spawn | Spawn Edit, Creature Spawn, Spawn Heatmap |
| Biome | Biome Setter, Height Biome Mapper |
| Environment | Temperature, Pressure, Toxic, Visibility, Air Pocket, Water Current, Environment Overlay |
| Utility | Export, Bin Importer, Material Batch, Undo History |
| Visualization | Chunk Grid, Depth Layer |
| Misc | Path, Wreckage, Bioluminescence |
| 생산성 | Scene Bookmark, Layer Batch, Scene Search, Prefab Batch |
| 디버그/시각화 | Draw Call Heatmap, Collider Visualizer, Light Range, UV Visualizer, Audio Visualizer |
| 자동화 | Scene Snapshot, Placement Rule, Mesh Optimizer |
| 임포트 | FBX Import, Texture Import, Texture Atlas |
| 렌더링/셰이더 | Shader Live Edit, Material Compare |
| 오디오 | Audio Mixer Preset |
| 빌드/배포 | Unused Asset, Asset Report |
| 협업 | Scene Changes, Object Owner |
| 물리 | Rigidbody Batch, Collider Fitter |
| LOD/Transform | LOD Generator, Lighting Preset, Static Flag, Object Snap, Transform Batch, Terrain Sculpt |
| 월드 | Terrain Forge, Minimap Baker, POI Placer, Spline Placement, Underwater Visualizer, Scatter Bake, World Audit, Streaming Simulator |
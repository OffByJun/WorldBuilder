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
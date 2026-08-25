# WorldBuilder

월드 Grid, Blender chunk 교환, Region streaming 및 데이터 기반 Water 시스템은 [Documentation/KR/WorldGridWater.md](Documentation/KR/WorldGridWater.md)와 [Documentation/KR/ChunkBlenderPipeline.md](Documentation/KR/ChunkBlenderPipeline.md)를 참고하세요. Blender 애드온은 [BlenderAddon/README.md](BlenderAddon/README.md)에 설치 및 export 계약이 정리되어 있습니다.

## KR

> 대규모 월드 제작을 위한 Unity Editor Framework

WorldBuilder는 Unity에서 대규모 월드를 효율적으로 제작하기 위한 에디터 확장 프레임워크입니다.

지형 편집, 바이옴 설정, 스폰 관리, 환경 구역 편집 등 월드 제작에 필요한 다양한 기능을 하나의 워크플로우로 제공합니다.

> **⚠️ 현재 활발히 개발 중인 프로젝트입니다. API 및 기능은 변경될 수 있습니다.**

## 주요 기능

### 월드 편집

* Terrain Paint
* Voxel Paint
* Mesh Edit
* Prefab Brush (프리셋 + Water Depth Mask)
* Spline Placement
* Minimap Baker (레이어 합성)
* POI Placer
* Underwater Visualizer
* Scatter Bake (Unity 단독 청크 베이크)
* World Audit

### 런타임 편집 (0.2.0+)

* `RuntimePlacementService` — GameObject 기반 런타임 배치/제거 + JSON 저장/복원
* `RuntimeWorldEditor` — 카메라 기반 고스트 프리뷰 에디터 컴포넌트 (배치/제거/이동)
* `WorldEntityRuntimeEditing` — DOTS 엔티티 런타임 스폰 파사드
* 자세한 내용은 `Documentation/KR/RuntimeEditing.md` 참조

### 환경 시스템

* 바이옴 관리
* Height → Biome 매핑
* Water Current
* Air Pocket
* Temperature Zone
* Pressure Zone
* Toxic Zone
* Visibility Zone

### 동굴 & 수중 엔진 (0.9.0+)

* 절차적 동굴 생성 — `CaveField` 스파게티 터널 + 케버턴 룸 감산 카빙 (`CaveShapeParams`)
* 동굴 프리셋 4종 — LimestoneCaves / LavaTubes / FloodedGrotto / AbyssalNetwork
* 환경 도메인 분류기 — OpenAir / Underwater / Underground / FloodedCave 한 번의 호출 판정
  (`UndergroundProbe` 복셀 인클로저 레이 + `WaterQueryService` 결합)
* 해저·동굴 바이옴 — Cave / CoralReef / KelpForest / AbyssalTrench → `BiomeType` 확장,
  Whittaker 해저 밴드 세분화, 스플랫 기본 매핑 연동

### 물 흐름 시스템 (0.10.0+)

* 바다 기본 해류 + `WaterCurrentZone` 흐름 오버라이드 존(폭포·소용돌이·강 하구)
* `WaterDrifter` 표류 시스템 — 부력 스프링, 흐름 가속, 침하 모드 (`WaterDrift.Integrate`)
* 지하수대 — `GroundwaterService`가 waterTableY 아래를 정수 처리 → 밀폐 동굴 자동 FloodedCave
* 날씨→수위 — `WaterLevelDriver`: 가뭄~홍수 강도 → 해수면 오프셋 + 유속 배수

### 저작 & 생태 배치 (0.11.0+)

* `RiverbedCarver` 강 하상 절삭 / `CaveField.CarveEntrances` 동굴 입구 자동 관통
* 메시→복셀 카빙 — Blender Cave Network를 스토어에 그대로 파기 (`WorldBuilder/Caves` 메뉴)
* 볼륨 스캐터 — 동굴 내부 바닥 배치(`VoxelVolumeScatter`) + Coral/Kelp/Cave 생태 프리셋 팩토리
* 수중 게이트 — 스캐터 규칙의 수심 밴드·유속 상한, 성장 단계(growth stages) 프리팹
* 계절 팔레트 `SeasonPalette`, 순찰 경로 `CreatureWaypointPath`, 미니맵 수심/동굴 레이어
* 통합 세이브 v2 — `WorldSaveService.SaveSnapshot`(배치+지형+extras), Terrain Audit 규칙 3종
* `WaterSurface` URP 셰이더(파도 변위·폼), `EnvironmentFxRig` 도메인별 대기 전환,
  `StreamingBudgetPreset` 스트리밍 예산

### 게임플레이 도구

* Spawn Editor
* Spawn Heatmap
* Creature Spawn Zone
* Event Trigger Zone
* Path Tool

### 런타임 데이터 파이프라인 (0.4.0+)

* WorldDataSnapshot 익스포트 → 런타임 로더(프리팹 바인딩)
* RuntimePlacementService → DOTS 엔티티 스폰 브리지

### 유틸리티

* Chunk Grid 시각화
* Depth Layer 시각화
* Material Batch
* Export Tool
* Undo History

### 확장 도구 (생산성 / 시각화 / 자동화)

> 자세한 내용은 `Documentation/KR/BuiltInTools.md` 참조 (총 30종)

* 생산성: Scene Bookmark, Layer Batch, Scene Search, Prefab Batch
* 디버그/시각화: Draw Call Heatmap, Collider Visualizer, Light Range, UV Visualizer, Audio Visualizer
* 자동화: Scene Snapshot, Placement Rule, Mesh Optimizer
* 임포트: FBX Import, Texture Import, Texture Atlas
* 렌더링/셰이더: Shader Live Edit, Material Compare
* 오디오: Audio Mixer Preset
* 빌드/배포: Unused Asset, Asset Report
* 협업: Scene Changes, Object Owner
* 물리: Rigidbody Batch, Collider Fitter
* LOD/Transform: LOD Generator, Lighting Preset, Static Flag, Object Snap, Transform Batch, Terrain Sculpt

## 설치

다음 문서를 참조하시기 바랍니다.
- Installation.md


## 문서

자세한 기술 문서는 `Documentation` 폴더에서 확인할 수 있습니다.

* 시작하기
* 설치 방법
* 아키텍처
* Tool Reference (BuiltInTools.md — 확장 도구 포함)
* API Reference

### 지형 엔진 (0.6.0+)

* 절차적 지형 생성 — 시드 fBm + 도메인 워프/리지드/테라스/아일랜드 파라미터
* 수적 드롭렛 + 열 침식 시뮬레이션 (결정적)
* Surface Nets 메싱 — 청크 심 자동 용접, 밀도 그래디언트 노멀
* Whittaker 바이옴 분류 + 고해상도 스플랫 (`HighResBiomeMap`)
* PCG 생태 규칙 엔진 — 고도/경사/바이옴 게이트 → 청크 베이크 연동
* 런타임 굴착/파괴 — `TerrainDeformer` 즉시 재메싱
* 워크벤치: **Terrain Forge** 도구

## 로드맵

* ~~Runtime Editing~~ (0.2.0 기반 구현 완료)
* 추가 편집 도구
* 시각화 기능 개선
* 성능 최적화

----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

## Eng

# WorldBuilder

> A Unity Editor Framework for building large-scale worlds.

WorldBuilder is a Unity Editor extension that provides a collection of tools for creating and editing large-scale worlds. It focuses on improving the world-building workflow by integrating terrain editing, biome management, spawn configuration, environmental zones, and various utility tools into a single framework.

> **⚠️ This project is under active development. APIs and features may change without notice.**

## Features

### World Editing

* Terrain Paint
* Voxel Paint
* Mesh Editing
* Prefab Brush (presets + Water Depth Mask)
* Spline Placement
* Minimap Baker (layer compositing)
* POI Placer
* Underwater Visualizer
* Scatter Bake (Unity-only chunk baking)
* World Audit

### Runtime Editing (0.2.0+)

* `RuntimePlacementService` — GameObject-based runtime placement/removal with JSON persistence
* `RuntimeWorldEditor` — camera-driven ghost preview editor component (place/remove/move)
* `WorldEntityRuntimeEditing` — thin DOTS entity spawn facade
* See `Documentation/KR/RuntimeEditing.md` for details

### Runtime Data Pipeline (0.4.0+)

* WorldDataSnapshot export → runtime loader with prefab bindings
* RuntimePlacementService → DOTS entity spawn bridge

### Environment

* Biome Management
* Height-to-Biome Mapping
* Water Current
* Air Pocket
* Temperature Zone
* Pressure Zone
* Toxic Zone
* Visibility Zone

### Caves & Underwater (0.9.0+)

* Procedural cave carving — `CaveField` warped spaghetti tunnels + depth-biased cavern rooms (`CaveShapeParams`)
* Four cave presets — LimestoneCaves / LavaTubes / FloodedGrotto / AbyssalNetwork
* Environment domain classifier — one call resolves OpenAir / Underwater / Underground / FloodedCave
  (`UndergroundProbe` voxel enclosure ray combined with `WaterQueryService`)
* Seafloor & cave biomes — Cave / CoralReef / KelpForest / AbyssalTrench added to `BiomeType`,
  refined Whittaker seafloor bands, splat default mapping wired up

### Water Flow (0.10.0+)

* Ocean base currents + `WaterCurrentZone` flow-override volumes (waterfalls, whirlpools, river mouths)
* `WaterDrifter` buoyancy/drift system — float-line spring, flow acceleration, sink mode
* Groundwater table — `GroundwaterService` floods everything below waterTableY; sealed caves classify as FloodedCave automatically
* Weather→water coupling — `WaterLevelDriver` maps drought/flood intensity to sea level offset + flow speed multiplier

### Authoring & Ecology Batches (0.11.0+/0.12.0+)

* Riverbed carving, automatic cave entrance shafts, mesh→voxel carving import (`WorldBuilder/Caves`)
* Volume scatter for cave interiors + Coral/Kelp/Cave ecology rule factories with underwater depth/flow gates and growth stages
* Season palette, creature patrol paths, minimap depth/cave layers, unified snapshot saves, terrain audit rules
* `WaterSurface` URP wave shader, domain-driven atmosphere rig, streaming budget presets

### Gameplay Tools

* Spawn Editor
* Spawn Heatmap
* Creature Spawn Zone
* Event Trigger Zone
* Path Tool

### Utilities

* Chunk Grid Visualization
* Depth Layer Visualization
* Material Batch
* Export Tools
* Undo History

### Extension Tools (Productivity / Visualization / Automation)

> See `Documentation/KR/BuiltInTools.md` for details (30 tools)

* Productivity: Scene Bookmark, Layer Batch, Scene Search, Prefab Batch
* Debug/Visualization: Draw Call Heatmap, Collider Visualizer, Light Range, UV Visualizer, Audio Visualizer
* Automation: Scene Snapshot, Placement Rule, Mesh Optimizer
* Import: FBX Import, Texture Import, Texture Atlas
* Rendering/Shader: Shader Live Edit, Material Compare
* Audio: Audio Mixer Preset
* Build/Deploy: Unused Asset, Asset Report
* Collaboration: Scene Changes, Object Owner
* Physics: Rigidbody Batch, Collider Fitter
* LOD/Transform: LOD Generator, Lighting Preset, Static Flag, Object Snap, Transform Batch, Terrain Sculpt

## Installation

Read this docs
- Installation.md

## Documentation

Detailed documentation is available in the `Documentation/` directory.

* Getting Started
* Installation
* Architecture
* Tool Reference (BuiltInTools.md — includes extension tools)
* API Reference

## Roadmap

* ~~Runtime Editing~~ (foundational support shipped in 0.2.0)
* Additional Builder Tools
* More Visualization Tools
* Performance Improvements

현재 라이선스는 지정되어 있지 않습니다.

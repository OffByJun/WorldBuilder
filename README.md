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
* Prefab Brush

### 환경 시스템

* 바이옴 관리
* Height → Biome 매핑
* Water Current
* Air Pocket
* Temperature Zone
* Pressure Zone
* Toxic Zone
* Visibility Zone

### 게임플레이 도구

* Spawn Editor
* Spawn Heatmap
* Creature Spawn Zone
* Event Trigger Zone
* Path Tool

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

## 로드맵

* Runtime Editing
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
* Prefab Brush

### Environment

* Biome Management
* Height-to-Biome Mapping
* Water Current
* Air Pocket
* Temperature Zone
* Pressure Zone
* Toxic Zone
* Visibility Zone

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

* Runtime Editing
* Additional Builder Tools
* More Visualization Tools
* Performance Improvements

현재 라이선스는 지정되어 있지 않습니다.

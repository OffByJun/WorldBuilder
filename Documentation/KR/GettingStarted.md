# Quick Start

이 문서는 WorldBuilder를 처음 사용하는 사용자를 위한 빠른 시작 가이드입니다.

약 5분 안에 기본적인 작업 흐름을 익힐 수 있습니다.

---

# 시작하기

WorldBuilder는 하나의 Editor Window를 중심으로 동작합니다.

각 기능은 Tool 형태로 제공되며,
사용자는 원하는 Tool을 선택하여 Scene을 편집합니다.

일반적인 작업 순서는 다음과 같습니다.

```
Open WorldBuilder

↓

Select Tool

↓

Edit World

↓

Preview Result

↓

Save Data

↓

Export
```

---

# 1. WorldBuilder 열기

Unity 상단 메뉴에서 WorldBuilder를 실행합니다.

예시

```
Window

↓

WorldBuilder
```

실행하면 WorldBuilder Editor Window가 열립니다.

---

# 2. Tool 선택

Editor Window의 Toolbar에는 사용할 수 있는 Tool들이 표시됩니다.

예를 들어

- Mesh Editor
- Terrain Painter
- Prefab Brush
- Spawn Editor

원하는 Tool을 선택하면 해당 Tool이 활성화됩니다.

동시에 하나의 Tool만 활성화됩니다.

---

# 3. Scene 편집

활성화된 Tool은 Scene View에서 동작합니다.

대부분의 Tool은

- 마우스 입력
- 선택
- 드래그
- 브러시

등을 이용하여 월드를 수정합니다.

편집 내용은 Runtime 데이터에 반영됩니다.

---

# 4. Tool 변경

작업 도중 언제든 Tool을 변경할 수 있습니다.

```
Mesh Editor

↓

Terrain Painter

↓

Prefab Brush
```

Tool이 변경되면

- 이전 Tool 종료
- 상태 저장
- 새 Tool 초기화

과정을 거쳐 안전하게 전환됩니다.

---

# 5. 데이터 저장

월드 편집이 끝나면 변경된 데이터를 저장합니다.

저장되는 데이터는

- World Data
- Spawn Data
- Tool Data

등 프로젝트 설정에 따라 달라질 수 있습니다.

---

# 6. Export

작업이 완료되면 Export 기능을 사용하여

런타임에서 사용할 수 있는 데이터로 변환합니다.

Export 과정은

```
Editor Data

↓

Validation

↓

Serialization

↓

Output
```

순서로 진행됩니다.

---

# 기본 Workflow

일반적인 작업 흐름은 다음과 같습니다.

```
Create Project

↓

Open WorldBuilder

↓

Create World

↓

Paint Terrain

↓

Place Prefabs

↓

Configure Spawns

↓

Validate

↓

Export
```

---

# Tool 사용 팁

### 작은 단위로 작업하기

대규모 변경보다
작은 단위로 편집하면 Undo 및 검증이 쉬워집니다.

---

### 자주 저장하기

장시간 작업 전에는 데이터를 저장하는 것을 권장합니다.

---

### Tool을 목적에 맞게 사용하기

각 Tool은 특정 작업을 수행하도록 설계되어 있습니다.

예를 들어

- Terrain 수정 → Terrain Painter
- 오브젝트 배치 → Prefab Brush
- 스폰 설정 → Spawn Editor

처럼 사용하는 것이 좋습니다.

---

# 문제 해결

## Tool이 동작하지 않습니다.

다음을 확인하십시오.

- Tool이 활성화되어 있는가
- Scene View가 선택되어 있는가
- 대상 오브젝트가 존재하는가

---

## 변경 내용이 보이지 않습니다.

다음을 확인하십시오.

- Scene이 저장되었는가
- Tool이 데이터를 갱신했는가
- Export가 필요한 작업인가

---

# 다음 단계

기본적인 사용법을 익혔다면

다음 문서를 읽는 것을 권장합니다.

- Architecture
- Tool System
- Built-in Tools
# WorldBuilder

WorldBuilder는 Unity용 월드 제작 패키지입니다.

이 패키지는 하나의 통합된 Editor Window 안에서 다양한 월드 제작 도구를 제공하며,
대규모 월드 제작 과정에서 반복되는 작업을 빠르고 일관성 있게 수행할 수 있도록 설계되었습니다.

WorldBuilder는 단순한 맵 페인터가 아니라,
월드 데이터를 생성하고 수정하며 관리하는 Editor Framework를 기반으로 합니다.

---

# 주요 기능

현재 패키지에는 다음과 같은 기능이 포함되어 있습니다.

- Mesh Editor
- Terrain Painter
- Prefab Brush
- Spawn Editor
- Air Pocket Tool
- Bin Importer
- Bioluminescence Tool

또한 패키지는

- Runtime 데이터 관리
- Tool Framework
- Export Pipeline
- World Data 관리

기능을 함께 제공합니다.

---

# 특징

WorldBuilder는 다음 원칙을 기반으로 제작되었습니다.

## Tool 기반 아키텍처

모든 기능은 독립적인 Tool로 구현됩니다.

각 Tool은 자신의 책임만 가지며,
공통 인터페이스를 통해 Editor Window와 통신합니다.

이를 통해 새로운 Tool을 쉽게 추가하거나 기존 Tool을 수정할 수 있습니다.

---

## Editor / Runtime 분리

패키지는 Editor와 Runtime 코드를 명확하게 분리합니다.

Editor는 월드를 제작하는 기능만 담당하며,

Runtime은

- 데이터 구조
- ScriptableObject
- 직렬화 데이터

등을 제공합니다.

---

## 확장성

새로운 Tool을 추가하기 위해 기존 Tool을 수정할 필요가 없습니다.

Tool을 구현하고 등록하면 WorldBuilder에서 사용할 수 있도록 설계되었습니다.

---

## 데이터 중심 설계

WorldBuilder는 Scene보다 데이터를 중심으로 동작합니다.

각 Tool은 월드 데이터를 수정하며,
Export 과정에서 필요한 형태로 데이터를 변환합니다.

---

# 대상 사용자

WorldBuilder는 다음과 같은 사용자에게 적합합니다.

- Unity Tool Programmer
- Technical Artist
- Level Designer
- World Designer

---

# 패키지 구성

```

Editor/
Runtime/
Documentation/
package.json

```

각 폴더의 역할은 다음과 같습니다.

|폴더|설명|
|---|---|
|Editor|모든 Editor 기능과 Tool 구현|
|Runtime|런타임 데이터 및 공용 타입|
|Documentation|패키지 문서|
|package.json|UPM 패키지 정보|

---

# 다음 문서

처음 사용하는 경우 다음 문서를 먼저 읽는 것을 권장합니다.

1. Installation
2. Quick Start
3. Tool System
4. Architecture
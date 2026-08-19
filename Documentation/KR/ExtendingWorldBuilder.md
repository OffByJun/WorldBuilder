# Extending WorldBuilder

WorldBuilder는 확장 가능한 Tool 기반 아키텍처를 사용합니다.

새로운 기능은 기존 코드를 수정하는 대신 새로운 Tool을 추가하는 방식으로 구현하는 것을 권장합니다.

---

# Extension Philosophy

WorldBuilder는 다음 원칙을 따릅니다.

- Open for Extension
- Closed for Modification

기존 Tool을 수정하기보다 새로운 Tool을 추가하는 것이 권장되는 개발 방식입니다.

---

# Extension Points

WorldBuilder에서 확장 가능한 대표적인 영역은 다음과 같습니다.

| 영역 | 설명 |
|------|------|
| Tool | 새로운 편집 기능 추가 |
| Editor Window | 새로운 UI 패널 추가 |
| Toolbar | Tool 등록 |
| Runtime Data | 새로운 데이터 타입 정의 |
| Export Pipeline | 새로운 Export 단계 추가 |
| Validation | 데이터 검증 규칙 추가 |

---

# Creating a New Tool

새로운 Tool을 추가하는 일반적인 과정입니다.

```text
Create Tool Class

↓

Implement Base Tool

↓

Register Tool

↓

Implement Scene GUI

↓

Connect Runtime Data

↓

Test
```

---

# Tool Responsibilities

하나의 Tool은 하나의 기능만 수행해야 합니다.

좋은 예

- Height Painter
- Cave Generator
- Resource Painter
- Spawn Editor

좋지 않은 예

- World Tool
    - Terrain
    - Spawn
    - Lighting
    - Export
    - Navigation

---

# Scene Interaction

Scene View에서 Tool은 다음 요소를 사용할 수 있습니다.

- Handles
- Gizmos
- Mouse Picking
- Selection
- Brush Preview

Scene GUI에서는 가능한 한 렌더링만 수행하고, 실제 데이터 변경은 명확한 사용자 입력 시점에 처리하는 것이 좋습니다.

---

# Runtime Integration

Tool은 Runtime 데이터를 직접 생성하거나 수정합니다.

```text
User Input

↓

Tool

↓

Runtime Data

↓

Save
```

Tool 내부에서 데이터를 별도로 보관하기보다 Runtime을 단일 진실 공급원(Single Source of Truth)으로 유지하는 것이 좋습니다.

---

# Undo Support

새로운 Tool은 가능한 경우 Unity Undo 시스템을 지원해야 합니다.

권장 사항

- 작업 시작 전 Undo 기록
- 하나의 작업을 하나의 Undo 단위로 관리
- 불필요한 Undo 생성 방지

---

# Validation

Tool은 잘못된 데이터를 생성하지 않도록 해야 합니다.

예를 들어

- Null 참조
- 음수 크기
- 범위를 벗어난 값
- 중복 데이터

등은 생성 단계에서 차단하는 것이 좋습니다.

---

# Performance Guidelines

대규모 월드를 고려하여 다음 사항을 권장합니다.

- 캐시 적극 활용
- GC Allocation 최소화
- Scene GUI에서 LINQ 사용 지양
- 반복 계산 캐싱
- 불필요한 객체 생성 방지

---

# Folder Structure

새로운 Tool은 기존 구조를 따르는 것을 권장합니다.

```text
Editor/

    Tools/

        MyTool/

            MyTool.cs
            MyToolView.cs
            MyToolIcon.png
```

관련 Runtime 데이터는 Runtime Assembly에 배치합니다.

---

# Testing Checklist

새로운 Tool을 추가한 후 다음 항목을 확인합니다.

- 정상적으로 등록되는가
- Toolbar에 표시되는가
- Scene View에서 동작하는가
- Runtime 데이터가 수정되는가
- 저장 및 Export가 가능한가
- Undo/Redo가 정상 동작하는가
- Console 오류가 없는가

---

# Best Practices

✔ 하나의 Tool은 하나의 책임만 가진다.

✔ Runtime과 Editor를 분리한다.

✔ Tool끼리 직접 의존하지 않는다.

✔ 데이터는 Runtime에서 관리한다.

✔ Export는 Runtime 데이터를 기준으로 수행한다.

✔ Tool은 가능한 한 Stateless하게 유지한다.

---

# Example Workflow

```text
Create Tool

↓

Register

↓

Activate

↓

Edit Data

↓

Save

↓

Export
```

---

# Summary

WorldBuilder는 확장성을 가장 중요한 설계 목표 중 하나로 합니다.

새로운 기능은 기존 구현을 변경하지 않고도 Tool을 추가하여 구현할 수 있으며, Runtime 중심의 데이터 구조를 유지함으로써 유지보수성과 확장성을 확보할 수 있습니다.
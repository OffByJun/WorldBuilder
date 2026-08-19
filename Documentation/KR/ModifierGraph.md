# Modifier Graph

Modifier Graph는 [Prefab Brush Pro++](PrefabBrush.md)의 배치 파라미터(위치 오프셋, 회전, 스케일)를 노드 기반으로 계산하는 시스템입니다.

UI Toolkit GraphView로 구현된 노드 에디터에서 스칼라(float) 노드를 연결하여 절차적 배치 규칙을 구성합니다.

| 항목 | 값 |
|------|----|
| 네임스페이스 | `WorldBuilder.Editor.PrefabBrush` |
| 어셈블리 | `WorldBuilder.Editor` |
| 에셋 메뉴 | `Create > WorldBuilder > Modifier Graph` |
| 에디터 메뉴 | `WorldBuilder > Modifier Graph` |
| 의존 패키지 | `Unity.Mathematics` (Simplex 노이즈) |

---

# 개요

Modifier Graph는 **스칼라 float 그래프**입니다. 각 노드는 `ModifierContext`를 입력받아 단일 `float` 값을 출력합니다. 노드는 상위 노드를 직접 참조하고 평가 시 재귀적으로 계산합니다.

그래프에는 3개의 출력 채널이 있으며, 각 채널은 X/Y/Z 3개의 float 입력을 가집니다.

| 채널 | 의미 | 미연결 기본값 |
|------|------|---------------|
| Position Offset | 배치 위치에 더해지는 오프셋 | 0 |
| Rotation | 오일러 각(도) 회전 | 0 |
| Scale | 곱해지는 스케일 | 1 |

브러시는 각 인스턴스마다 채널을 평가하여 다음과 같이 적용합니다.

```
position += EvaluatePositionOffset(graph, ctx)
rotation *= Quaternion.Euler(EvaluateRotation(graph, ctx))
scale     = Vector3.Scale(scale, EvaluateScale(graph, ctx))
```

---

# ModifierContext

평가에 사용되는 입력 컨텍스트입니다. 모든 노드는 이 구조체만으로 출력을 계산합니다.

```csharp
[Serializable]
public struct ModifierContext
{
    public Vector3 worldPosition;
    public Vector3 brushCenter;
    public float brushRadius;
    public Vector3 surfaceNormal;
    public BiomeType biome;
    public int seed;
}
```

| 필드 | 설명 |
|------|------|
| worldPosition | 평가 대상 인스턴스의 월드 위치 |
| brushCenter | 스트로크 중심 |
| brushRadius | 브러시 반경 |
| surfaceNormal | 표면 법선 |
| biome | 해당 위치의 바이옴 |
| seed | 스트로크 시드 |

---

# 평가 모델

## 노드 인터페이스

```csharp
public interface IModifierNode
{
    string NodeName { get; }
    float Evaluate(ModifierContext ctx);
}
```

## 입력 배선과 재귀

`ClampNode`, `LerpNode` 등 상위 입력이 필요한 노드는 상위 노드를 `[SerializeReference]`로 직접 참조합니다. `Evaluate` 내부에서 상위 노드의 `Evaluate`를 재귀 호출하므로 별도의 위상 정렬이 필요 없습니다.

무한 재귀를 방지하기 위해 두 가지 장치를 둡니다.

- **에디터 사이클 방지.** `GetCompatiblePorts`가 사이클을 만드는 연결을 허용하지 않습니다.
- **평가 깊이 가드.** 재귀 깊이가 128을 초과하면 `0`을 반환합니다.

> **참고**
>
> 모든 노드는 동일한 `ModifierContext`에 대해 동일한 출력을 반환하는 순수 함수입니다(LSP). 노드 내부에서 난수를 사용하지 않습니다.

## 채널 평가

```csharp
public static class ModifierGraphEvaluator
{
    public static Vector3 EvaluatePositionOffset(ModifierGraph graph, ModifierContext ctx);
    public static Vector3 EvaluateRotation(ModifierGraph graph, ModifierContext ctx);
    public static Vector3 EvaluateScale(ModifierGraph graph, ModifierContext ctx);
}
```

각 메서드는 채널의 X/Y/Z 입력 노드를 각각 평가하여 `Vector3`로 조합합니다. 입력이 없는 축은 채널 기본값(Position/Rotation = 0, Scale = 1)을 사용합니다.

---

# 노드 카탈로그

노드는 카테고리별로 색상이 구분됩니다.

| 카테고리 | 색상 |
|----------|------|
| Basic | `#2D2D30` (회색) |
| Noise | `#1A6B3C` (초록) |
| Math | `#1A3B6B` (파랑) |
| Spatial | `#6B3B1A` (주황) |
| Mask | `#6B1A1A` (빨강) |

## Basic

### AddNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1) |
| 파라미터 | `float value` |
| 출력 | `Evaluate(In) + value` |

### MultiplyNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1, 기본 1) |
| 파라미터 | `float value` (기본 1) |
| 출력 | `Evaluate(In) * value` |

### OverrideNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `float value` |
| 출력 | `value` (상수 소스) |

## Noise

공통 파라미터 `NoiseSettings`를 사용합니다.

```csharp
[Serializable]
public struct NoiseSettings
{
    public float scale;
    public float amplitude;
    public Vector2 offset;
}
```

### PerlinNoiseNode

| 항목 | 값 |
|------|----|
| 출력 | `Mathf.PerlinNoise((x + offset.x)/scale, (z + offset.y)/scale) * amplitude` |
| 범위 | `0 ~ amplitude` |

### SimplexNoiseNode

| 항목 | 값 |
|------|----|
| 구현 | `Unity.Mathematics.noise.snoise(float2)` |
| 출력 | `(-1~1 → 0~1 리매핑) * amplitude` |

### VoronoiNoiseNode

| 항목 | 값 |
|------|----|
| 추가 파라미터 | `int cellCount` (좌표 주파수 배수) |
| 출력 | 최근접 셀(F1) 거리 기반 값 `* amplitude` |
| 특징점 | 해시 기반 결정적 |

### FractalNoiseNode

| 항목 | 값 |
|------|----|
| 추가 파라미터 | `int octaves`(1–8), `float lacunarity`(기본 2), `float persistence`(기본 0.5) |
| 출력 | 옥타브별 Perlin 중첩(가중 평균 정규화) `* amplitude` |

## Math

### ClampNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1) |
| 파라미터 | `float min`, `float max` |
| 출력 | `Mathf.Clamp(Evaluate(In), min, max)` |

### RemapNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1) |
| 파라미터 | `inMin`, `inMax`, `outMin`, `outMax` |
| 출력 | `Mathf.Lerp(outMin, outMax, InverseLerp(inMin, inMax, Evaluate(In)))` |

### AbsNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1) |
| 출력 | `Mathf.Abs(Evaluate(In))` |

### PowerNode

| 항목 | 값 |
|------|----|
| 입력 | `In` (1) |
| 파라미터 | `float exponent` (기본 2) |
| 출력 | `Mathf.Pow(Evaluate(In), exponent)` |

### LerpNode

| 항목 | 값 |
|------|----|
| 입력 | `A`, `B`, `T` (3) |
| 파라미터 | `float t` (0–1, `T` 미연결 시 사용) |
| 출력 | `Mathf.Lerp(Evaluate(A), Evaluate(B), T 연결 시 Evaluate(T) 아니면 t)` |

## Spatial

### PositionToValueNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `Axis axis` (X/Y/Z) |
| 출력 | `ctx.worldPosition[axis]` |

### DistanceFromCenterNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `bool normalize` |
| 출력 | `normalize ? distance / brushRadius : distance` |

## Mask

### HeightMaskNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `minHeight`, `maxHeight`, `falloff` |
| 출력 | `min/max ± falloff` 구간의 smoothstep 밴드 마스크 (0~1) |

### SlopeMaskNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `maxAngle`, `falloff` |
| 출력 | `1 - SmoothStep(InverseLerp(maxAngle - falloff, maxAngle + falloff, angle))` |

### BiomeMaskNode

| 항목 | 값 |
|------|----|
| 입력 | 없음 |
| 파라미터 | `BiomeType targetBiome` |
| 출력 | `ctx.biome == targetBiome ? 1 : 0` |

---

# 노드 에디터 사용법

1. `WorldBuilder > Modifier Graph`로 에디터를 엽니다. (또는 Prefab Brush의 **Open Modifier Graph** 버튼)
2. 상단 **Graph** 필드에 `ModifierGraph` 에셋을 지정합니다.
3. 그래프 영역에서 **우클릭 > 카테고리 > 노드**로 노드를 추가합니다.
4. 노드 출력 포트를 다른 노드의 입력 포트 또는 채널(Position/Rotation/Scale)의 X/Y/Z 포트로 드래그하여 연결합니다(Bézier 곡선).
5. 노드를 선택하면 **우측 인스펙터 패널**에 파라미터가 표시됩니다.
6. **Save**로 에셋을 저장합니다. 모든 편집은 Undo로 취소할 수 있습니다.

> **참고**
>
> 채널 출력 노드는 삭제할 수 없습니다. 입력 포트는 단일 연결(Single), 출력 포트는 다중 연결(Multi)을 허용합니다.

---

# 확장 (새 노드 추가)

새 노드를 추가하려면 `ModifierNodeBase`를 상속하는 파일 하나만 추가하면 됩니다. 기존 Evaluator/Registry/Window 코드는 수정하지 않습니다(OCP).

```csharp
[Serializable]
public sealed class StepNode : ModifierNodeBase
{
    [HideInInspector] [SerializeReference] private IModifierNode input;
    public float threshold = 0.5f;

    public override string NodeName => "Step";
    public override ModifierNodeCategory Category => ModifierNodeCategory.Math;
    public override ModifierNodeBase CreateInstance() => new StepNode();

    public override int InputPortCount => 1;
    public override IModifierNode GetInput(int index) => input;
    public override void SetInput(int index, IModifierNode node) => input = node;

    protected override float EvaluateInternal(ModifierContext ctx)
        => EvaluateInput(input, ctx) >= threshold ? 1f : 0f;
}
```

그 후 `ModifierGraphBootstrap`에 한 줄을 등록합니다.

```csharp
ModifierNodeRegistry.Register(new StepNode());
```

> **참고**
>
> 입력 참조 필드는 `[HideInInspector]`로 표시하여 우측 인스펙터 패널이 스칼라 파라미터만 표시하도록 합니다. 노드 생성은 리플렉션이 아닌 `CreateInstance()`로 처리됩니다.

---

# 관련 문서

- [Prefab Brush Pro++](PrefabBrush.md)
- [Scripting API](ScriptingAPI.md)
- [Extending WorldBuilder](ExtendingWorldBuilder.md)

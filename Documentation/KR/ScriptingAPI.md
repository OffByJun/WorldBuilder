# Scripting API Reference

이 문서는 Prefab Brush Pro++와 Modifier Graph 시스템의 Public 스크립팅 API를 설명합니다.

> **참고**
>
> 이 레퍼런스는 `WorldBuilder.Editor` 어셈블리(에디터 전용)를 대상으로 합니다. `Internal`로 표시되지 않은 타입만 Public API로 간주합니다.

## 네임스페이스

| 네임스페이스 | 내용 |
|--------------|------|
| `WorldBuilder.Editor` | 공용 추상화(`IBiomeMap`, `ChunkBiomeMapAdapter`) |
| `WorldBuilder.Editor.PrefabBrush` | 브러시 도구, 설정, Modifier Graph |

---

# WorldBuilder.Editor

## interface IBiomeMap

청크 좌표 단위의 바이옴 조회/설정 추상화입니다.

```csharp
public interface IBiomeMap
{
    BiomeType GetBiome(Vector3Int chunkCoord);
    void SetBiome(Vector3Int chunkCoord, BiomeType biome);
}
```

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `GetBiome(Vector3Int chunkCoord)` | `BiomeType` | 청크 좌표의 바이옴을 반환합니다. |
| `SetBiome(Vector3Int chunkCoord, BiomeType biome)` | `void` | 청크 좌표의 바이옴을 설정합니다. |

---

## class ChunkBiomeMapAdapter

`IChunkBiomeMap`을 `IBiomeMap`으로 변환하는 어댑터입니다.

```csharp
public sealed class ChunkBiomeMapAdapter : IBiomeMap
```

### Constructor

```csharp
public ChunkBiomeMapAdapter(IChunkBiomeMap inner, BiomeType fallback = BiomeType.Ocean)
```

| 파라미터 | 설명 |
|----------|------|
| `inner` | 래핑할 청크 바이옴 맵 |
| `fallback` | 미설정 청크에 대해 반환할 기본 바이옴 |

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `GetBiome(Vector3Int chunkCoord)` | `BiomeType` | 설정되어 있으면 해당 바이옴, 아니면 `fallback`을 반환합니다. |
| `SetBiome(Vector3Int chunkCoord, BiomeType biome)` | `void` | 내부 맵에 바이옴을 설정합니다. |

---

# WorldBuilder.Editor.PrefabBrush

## class PrefabBrushTool

프리팹 스캐터 브러시 도구입니다. `IWorldBuilderTool`과 `IRaycastConsumer`를 구현합니다.

```csharp
public sealed class PrefabBrushTool : IWorldBuilderTool, IRaycastConsumer
```

### Constructor

```csharp
public PrefabBrushTool(IBiomeMap biomeMap)
```

| 파라미터 | 설명 |
|----------|------|
| `biomeMap` | 바이옴 마스크 평가에 사용되는 바이옴 맵 추상 |

### Properties

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `ToolName` | `string` | `"Prefab Brush Pro++"` |
| `ToolIcon` | `Texture2D` | 도구 아이콘(`null`) |

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `OnEnable()` | `void` | 설정 로드, 프리뷰 머티리얼 생성, Spatial Hash 재구축 |
| `OnSceneGUI()` | `void` | 브러시 디스크/프리뷰 렌더링 및 입력 처리 |
| `CreateInspectorGUI()` | `VisualElement` | Inspector UI(UI Toolkit) 생성 |
| `TryRaycast(out RaycastHit hit)` | `bool` | 마우스 위치 레이캐스트 |

---

## class PrefabBrushSettings

브러시 상태를 영속화하는 ScriptableObject입니다. 모든 편집은 `Undo.RecordObject`로 기록됩니다.

```csharp
public sealed class PrefabBrushSettings : ScriptableObject
```

### Fields

| 필드 | 타입 | 기본값 | 설명 |
|------|------|--------|------|
| `seed` | `int` | `Random.Range(0, 99999)` | 브러시 전역 시드 |
| `brushRadius` | `float` | `3` | 브러시 반경 |
| `brushDensity` | `int` | `10` | 스트로크당 인스턴스 수 |
| `eraseMode` | `bool` | `false` | 지우기 모드 |
| `chunkSize` | `float` | `16` | 바이옴 청크 좌표 변환 크기 |
| `alignToNormal` | `bool` | `true` | 표면 법선 정렬 |
| `randomYaw` | `bool` | `true` | 무작위 Y축 회전 |
| `scaleRange` | `Vector2` | `(1, 1)` | 균등 스케일 최소/최대 |
| `duplicateSharedScriptableObjects` | `bool` | `true` | 공유 SO 독립 복사 |
| `prefabEntries` | `List<PrefabEntry>` | 빈 목록 | 프리팹/가중치 항목 |
| `mask` | `BrushMask` | 기본값 | 마스크 설정 |
| `modifierGraph` | `ModifierGraph` | `null` | 연결된 Modifier Graph |
| `strokes` | `List<BrushStroke>` | 빈 목록 | 기록된 스트로크 |

---

## class PrefabBrushSettingsLocator

설정 에셋을 로드하거나 생성합니다.

```csharp
public static class PrefabBrushSettingsLocator
```

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `LoadOrCreate()` | `PrefabBrushSettings` | 기존 설정을 로드하거나 `Assets/WorldBuilder/PrefabBrushSettings.asset`을 생성합니다. |

---

## struct PrefabEntry

```csharp
[Serializable]
public struct PrefabEntry
{
    public GameObject prefab;
    public float weight;
}
```

| 필드 | 설명 |
|------|------|
| `prefab` | 배치할 프리팹 |
| `weight` | 가중치(0보다 커야 선택 대상) |

---

## class BrushMask

```csharp
[Serializable]
public class BrushMask
{
    public bool useHeightMask;
    public float minHeight;
    public float maxHeight;
    public bool useSlopeMask;
    public float maxSlopeAngle;
    public bool useBiomeMask;
    public BiomeType allowedBiome;
}
```

| 필드 | 설명 |
|------|------|
| `useHeightMask` | 고도 마스크 사용 |
| `minHeight` / `maxHeight` | 허용 고도 범위 |
| `useSlopeMask` | 경사 마스크 사용 |
| `maxSlopeAngle` | 허용 최대 경사각(도) |
| `useBiomeMask` | 바이옴 마스크 사용 |
| `allowedBiome` | 허용 바이옴 |

---

## struct BrushStroke

```csharp
[Serializable]
public struct BrushStroke
{
    public int seed;
    public Vector3 center;
    public float radius;
    public int density;
}
```

| 필드 | 설명 |
|------|------|
| `seed` | 스트로크 시드 |
| `center` | 스트로크 중심 |
| `radius` | 브러시 반경 |
| `density` | 인스턴스 수 |

---

## struct BrushContext

배치 파라미터를 담는 가변 구조체입니다.

```csharp
public struct BrushContext
{
    public Vector3 position;
    public Vector3 normal;
    public Quaternion rotation;
    public Vector3 scale;
}
```

---

## struct BrushPlacement

확정된 단일 인스턴스 배치 정보입니다.

```csharp
public struct BrushPlacement
{
    public GameObject prefab;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
}
```

---

## class SpatialHash&lt;T&gt;

균등 격자 기반 공간 해시입니다.

```csharp
public sealed class SpatialHash<T>
```

### Constructor

```csharp
public SpatialHash(float cellSize)
```

| 파라미터 | 설명 |
|----------|------|
| `cellSize` | 격자 셀 크기(최소 `0.0001`로 클램프) |

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `Add(Vector3 position, T item)` | `void` | 항목을 위치에 등록합니다. |
| `Remove(Vector3 position, T item)` | `void` | 해당 셀에서 항목을 제거합니다. |
| `Clear()` | `void` | 모든 항목을 제거합니다. |
| `Query(Vector3 center, float radius)` | `List<T>` | 반경 내 항목을 반환합니다. |

---

## struct ModifierContext

Modifier Graph 평가 입력입니다. 자세한 내용은 [ModifierGraph.md](ModifierGraph.md)를 참고하세요.

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

---

## interface IModifierNode

```csharp
public interface IModifierNode
{
    string NodeName { get; }
    float Evaluate(ModifierContext ctx);
}
```

| 멤버 | 반환 | 설명 |
|------|------|------|
| `NodeName` | `string` | 노드 표시 이름 |
| `Evaluate(ModifierContext ctx)` | `float` | 컨텍스트로부터 스칼라 값을 계산합니다. |

---

## abstract class ModifierNodeBase

모든 노드의 기반 클래스입니다. 에디터 메타데이터, 입력 포트 API, 재귀 깊이 가드를 제공합니다.

```csharp
[Serializable]
public abstract class ModifierNodeBase : IModifierNode
```

### Properties

| 프로퍼티 | 타입 | 설명 |
|----------|------|------|
| `Guid` | `string` | 노드 고유 ID |
| `GraphPosition` | `Vector2` | 그래프 뷰 좌표 |
| `NodeName` | `string` | 표시 이름(추상) |
| `Category` | `ModifierNodeCategory` | 노드 카테고리(추상) |

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `CreateInstance()` | `ModifierNodeBase` | 동일 타입의 새 인스턴스를 생성합니다(추상, 리플렉션 미사용). |
| `Evaluate(ModifierContext ctx)` | `float` | 깊이 가드(128) 적용 후 `EvaluateInternal`을 호출합니다. |
| `EvaluateInternal(ModifierContext ctx)` | `float` | 실제 계산 로직(추상, `protected`). |
| `InputPortCount` | `int` | 입력 포트 수(가상, 기본 0). |
| `GetInputPortName(int index)` | `string` | 입력 포트 이름(가상). |
| `GetInput(int index)` | `IModifierNode` | 입력 노드 조회(가상). |
| `SetInput(int index, IModifierNode node)` | `void` | 입력 노드 설정(가상). |
| `EvaluateInput(IModifierNode node, ModifierContext ctx, float fallback = 0)` | `float` | 입력 노드를 평가하거나 `fallback`을 반환하는 `protected static` 헬퍼. |

---

## enum ModifierChannel

```csharp
public enum ModifierChannel { PositionOffset, Rotation, Scale }
```

## enum Axis

```csharp
public enum Axis { X, Y, Z }
```

## enum ModifierNodeCategory

```csharp
public enum ModifierNodeCategory { Basic, Noise, Math, Spatial, Mask }
```

---

## struct NoiseSettings

```csharp
[Serializable]
public struct NoiseSettings
{
    public float scale;
    public float amplitude;
    public Vector2 offset;

    public float SafeScale { get; }
}
```

| 멤버 | 설명 |
|------|------|
| `scale` | 노이즈 스케일 |
| `amplitude` | 출력 진폭 |
| `offset` | 좌표 오프셋 |
| `SafeScale` | `Mathf.Max(0.0001f, scale)` |

---

## class ModifierGraph

노드 그래프를 저장하는 ScriptableObject입니다.

```csharp
[CreateAssetMenu(fileName = "ModifierGraph", menuName = "WorldBuilder/Modifier Graph")]
public sealed class ModifierGraph : ScriptableObject
```

### Fields

| 필드 | 타입 | 설명 |
|------|------|------|
| `nodes` | `List<IModifierNode>` | 그래프의 모든 노드(`[SerializeReference]`) |

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `GetChannelInput(ModifierChannel channel, int axis)` | `IModifierNode` | 채널/축에 연결된 입력 노드를 반환합니다. |
| `SetChannelInput(ModifierChannel channel, int axis, IModifierNode node)` | `void` | 채널/축 입력 노드를 설정합니다. |
| `RemoveNode(IModifierNode node)` | `void` | 노드를 제거하고 모든 참조를 정리합니다. |
| `ChannelGuid(ModifierChannel channel, int axis)` | `string` | 채널 식별자(static). |
| `IsChannelGuid(string guid)` | `bool` | 채널 식별자 여부(static). |

---

## class ModifierGraphEvaluator

```csharp
public static class ModifierGraphEvaluator
```

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `EvaluatePositionOffset(ModifierGraph graph, ModifierContext ctx)` | `Vector3` | 위치 오프셋 채널 평가(기본 0) |
| `EvaluateRotation(ModifierGraph graph, ModifierContext ctx)` | `Vector3` | 회전 채널 평가(기본 0) |
| `EvaluateScale(ModifierGraph graph, ModifierContext ctx)` | `Vector3` | 스케일 채널 평가(기본 1) |

---

## class ModifierNodeRegistry

노드 프로토타입을 수동 등록하는 레지스트리입니다(리플렉션 미사용).

```csharp
public static class ModifierNodeRegistry
```

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `GetAll()` | `IReadOnlyList<IModifierNode>` | 등록된 프로토타입 목록 |
| `Register(IModifierNode node)` | `void` | 프로토타입 등록 |
| `Clear()` | `void` | 등록 목록 초기화 |

> **참고**
>
> `ModifierGraphBootstrap`(`[InitializeOnLoad]`)이 도메인 로드 시 모든 내장 노드를 등록합니다.

---

## class ModifierGraphWindow

Modifier Graph 노드 에디터 창입니다.

```csharp
public sealed class ModifierGraphWindow : EditorWindow
```

### Methods

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `Open()` | `void` | 빈 에디터 창을 엽니다(`MenuItem "WorldBuilder/Modifier Graph"`). |
| `Open(ModifierGraph target)` | `void` | 지정 그래프를 로드하여 엽니다(static). |

---

## class ModifierGraphView

```csharp
public sealed class ModifierGraphView : GraphView
```

`UnityEditor.Experimental.GraphView` 기반 그래프 뷰입니다. 노드 생성, 연결, 사이클 방지, 선택 통지를 담당합니다.

| 멤버 | 타입 | 설명 |
|------|------|------|
| `NodeSelected` | `Action<ModifierNodeBase>` | 노드 선택 시 호출되는 콜백 |
| `Populate(ModifierGraph target)` | `void` | 그래프를 뷰로 재구성합니다. |

---

# 내장 노드 타입

모두 `WorldBuilder.Editor.PrefabBrush` 네임스페이스의 `ModifierNodeBase` 파생 `sealed class`입니다. 파라미터와 출력은 [ModifierGraph.md](ModifierGraph.md)의 노드 카탈로그를 참고하세요.

| 카테고리 | 타입 |
|----------|------|
| Basic | `AddNode`, `MultiplyNode`, `OverrideNode` |
| Noise | `PerlinNoiseNode`, `SimplexNoiseNode`, `VoronoiNoiseNode`, `FractalNoiseNode` |
| Math | `ClampNode`, `RemapNode`, `AbsNode`, `PowerNode`, `LerpNode` |
| Spatial | `PositionToValueNode`, `DistanceFromCenterNode` |
| Mask | `HeightMaskNode`, `SlopeMaskNode`, `BiomeMaskNode` |

---

# 예제

## 도구 등록

```csharp
using WorldBuilder.Editor;
using WorldBuilder.Editor.PrefabBrush;

IChunkBiomeMap chunkMap = new ChunkBiomeMap();
IBiomeMap biomeMap = new ChunkBiomeMapAdapter(chunkMap);
WorldBuilderToolRegistry.Register(new PrefabBrushTool(biomeMap));
```

## 그래프 평가

```csharp
ModifierContext ctx = new ModifierContext
{
    worldPosition = worldPos,
    brushCenter = center,
    brushRadius = radius,
    surfaceNormal = normal,
    biome = biomeMap.GetBiome(coord),
    seed = strokeSeed
};

Vector3 offset = ModifierGraphEvaluator.EvaluatePositionOffset(graph, ctx);
Vector3 rotation = ModifierGraphEvaluator.EvaluateRotation(graph, ctx);
Vector3 scale = ModifierGraphEvaluator.EvaluateScale(graph, ctx);
```

---

# 관련 문서

- [Prefab Brush Pro++](PrefabBrush.md)
- [Modifier Graph](ModifierGraph.md)
- [Architecture](Architecture.md)

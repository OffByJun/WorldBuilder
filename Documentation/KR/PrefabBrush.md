# Prefab Brush Pro++

Prefab Brush Pro++는 Scene View에서 프리팹을 브러시로 스캐터(scatter) 배치하는 WorldBuilder 에디터 도구입니다.

이 도구는 결정적(deterministic) 난수 시스템, 가중치 기반 프리팹 선택, GPU 인스턴스 프리뷰, 마스크 필터링, 스트로크 재현(replay), Spatial Hash 기반 지우기 최적화, 그리고 노드 기반 [Modifier Graph](ModifierGraph.md) 연동을 제공합니다.

| 항목 | 값 |
|------|----|
| 네임스페이스 | `WorldBuilder.Editor.PrefabBrush` |
| 어셈블리 | `WorldBuilder.Editor` |
| 도구 이름 | `Prefab Brush Pro++` |
| 등록 위치 | `WorldBuilderBootstrap` |
| 설정 에셋 | `Assets/WorldBuilder/PrefabBrushSettings.asset` |

> **참고**
>
> 이 도구는 에디터 전용입니다. 모든 상태는 `PrefabBrushSettings`(ScriptableObject)에 영속화되며 Unity의 Undo 시스템과 통합됩니다.

---

# 개요

`PrefabBrushTool`은 `IWorldBuilderTool`을 구현하며 WorldBuilder 창의 도구 목록에 표시됩니다. Scene View에서 커서 위치에 브러시 디스크를 그리고, 마우스 좌클릭 시 디스크 범위 내부에 프리팹 인스턴스를 배치합니다.

도구의 모든 튜닝 값은 `[SerializeField]` 또는 UI Toolkit 바인딩을 통해 노출되며, IMGUI를 사용하지 않습니다.

## 핵심 개념

| 개념 | 설명 |
|------|------|
| Stroke | 한 번의 좌클릭으로 발생하는 배치 단위. `BrushStroke`로 기록됩니다. |
| Seed | 브러시 전역 시드. 각 스트로크는 시드와 중심 좌표로부터 파생된 스트로크 시드를 사용합니다. |
| Placement | 스트로크가 생성하는 개별 인스턴스의 배치 정보(`BrushPlacement`). |
| Modifier Graph | 배치 위치/회전/스케일을 후처리하는 노드 그래프. |
| Mask | 고도/경사/바이옴 조건으로 배치 위치를 필터링하는 규칙(`BrushMask`). |

---

# 결정적 브러시 시스템

Prefab Brush Pro++는 전역 `UnityEngine.Random`을 사용하지 않습니다. 모든 난수는 시드 기반 `System.Random` 인스턴스에서 생성됩니다.

## 스트로크 시드 파생

스트로크 시드는 브러시 전역 시드와 브러시 중심 좌표를 결합하여 계산됩니다. 좌표는 0.1 단위로 양자화되어 프레임 간 부동소수점 흔들림에 영향을 받지 않습니다.

```
strokeSeed = CombineSeed(settings.seed, center)
```

이 설계는 다음을 보장합니다.

- **동일 시드 + 동일 파라미터 = 동일 결과.** 같은 위치에서 같은 설정으로 배치하면 항상 동일한 배치가 생성됩니다.
- **프리뷰 = 실제 배치.** 프리뷰와 실제 배치가 동일한 스트로크 시드를 사용하므로 미리보기와 결과가 일치합니다.

## 난수 소비 순서

각 인스턴스는 단일 `System.Random` 인스턴스에서 다음 순서로 난수를 소비합니다. 마스크 탈락 여부와 관계없이 소비 순서가 고정되어 결정성이 유지됩니다.

| 순서 | 용도 | 소비량 |
|------|------|--------|
| 1 | 가중치 기반 프리팹 선택 | 1 |
| 2 | 디스크 내부 위치 오프셋 | 2 |
| 3 | Y축 회전(yaw) | 1 |
| 4 | 균등 스케일 | 1 |

> **참고**
>
> 마스크 검사는 난수를 모두 소비한 *후*에 수행됩니다. 따라서 일부 인스턴스가 마스크로 제외되어도 다른 인스턴스의 난수 시퀀스는 변하지 않습니다.

## Randomize

Inspector의 **Randomize** 버튼은 `Random.Range(0, 99999)`로 새 전역 시드를 생성합니다. 시드 변경은 `Undo.RecordObject`로 기록되어 Undo/Redo로 복원됩니다.

---

# 가중치 시스템

프리팹은 `PrefabEntry` 목록으로 관리되며, 각 항목은 가중치를 가집니다.

```csharp
[Serializable]
public struct PrefabEntry
{
    public GameObject prefab;
    public float weight;
}
```

선택 알고리즘은 다음과 같습니다.

1. `prefab != null && weight > 0` 인 항목만 유효 항목으로 수집합니다.
2. 유효 항목의 가중치 합을 계산합니다(정규화 자동 처리).
3. `rng.NextDouble() * totalWeight` 값으로 누적 룰렛 선택을 수행합니다.

Inspector에서는 각 항목에 0~1 범위의 weight 슬라이더가 표시됩니다.

> **참고**
>
> 유효 항목이 하나도 없으면 스트로크는 인스턴스를 생성하지 않으며, `OnSceneGUI`는 프리뷰를 그리지 않고 조기 반환합니다(지우기 모드 제외).

---

# 마스크 시스템

`BrushMask`는 배치 위치를 필터링합니다. 각 마스크는 독립적으로 활성화할 수 있으며, 조건을 만족하지 않는 위치는 배치에서 제외됩니다.

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

| 마스크 | 조건 |
|--------|------|
| Height | `minHeight <= position.y <= maxHeight` |
| Slope | `Vector3.Angle(surfaceNormal, Vector3.up) <= maxSlopeAngle` |
| Biome | `biomeMap.GetBiome(chunkCoord) == allowedBiome` |

바이옴 조회는 `IBiomeMap` 추상에만 의존하며(DIP), 월드 좌표는 `ChunkCoordCalculator.ToChunkCoord(position, chunkSize)`로 청크 좌표로 변환됩니다.

> **참고**
>
> 바이옴 마스크는 `BiomeSetterTool` 등이 채운 인메모리 청크 맵을 참조합니다. 설정되지 않은 청크는 어댑터의 fallback 값(`BiomeType.Ocean`)으로 반환됩니다.

---

# GPU 인스턴스 프리뷰

프리뷰는 인스턴스를 실제로 생성하지 않고 `Graphics.DrawMeshInstanced`로 매 프레임 GPU 렌더링됩니다.

- 배치 예정 위치의 메시를 메시별로 그룹화하여 인스턴싱 배치(최대 1023개/배치)로 렌더링합니다.
- 반투명 오버레이를 위해 URP Unlit 머티리얼과 `MaterialPropertyBlock`을 사용합니다.
- 실제 배치는 좌클릭 시에만 `PrefabUtility.InstantiatePrefab`을 호출합니다.

> **참고**
>
> 프리뷰는 각 프리팹의 `MeshFilter`를 순회하여 루트 기준 상대 변환을 계산합니다. 서브메시는 인덱스 0만 렌더링되며, 멀티 서브메시 메시의 정확한 머티리얼 표현은 보장되지 않습니다.

---

# Spatial Hash 지우기

지우기 모드에서는 `Physics.OverlapSphere` 대신 `SpatialHash<GameObject>`를 사용합니다.

- 도구 활성화 시 씬의 모든 프리팹 인스턴스 루트가 Spatial Hash에 등록됩니다.
- 배치 시 인스턴스가 추가되고, 지우기 시 반경 조회 후 제거됩니다.
- 셀 크기는 `4` 단위 고정이며, `Query`는 임의 반경에 대해 인접 셀을 검사합니다.

---

# 스캐터 시드 재현

모든 스트로크는 `BrushStroke`로 기록되어 완전한 재현이 가능합니다.

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

| 기능 | 설명 |
|------|------|
| Replay | 기록된 모든 스트로크를 재실행하여 동일한 월드를 재구성합니다. |
| Export | `BrushStroke[]`를 JSON 파일로 저장합니다(`JsonUtility`). |
| Import | JSON에서 스트로크를 로드한 뒤 자동으로 Replay합니다. |
| Clear | 기록된 스트로크를 비웁니다. |

Replay와 Import는 단일 Undo 그룹으로 묶여 한 번의 Undo로 취소됩니다.

---

# ScriptableObject 공유 처리

배치된 인스턴스가 공유 ScriptableObject를 참조하는 경우, 각 인스턴스가 독립 인스턴스를 갖도록 복사합니다.

- `duplicateSharedScriptableObjects`가 활성화되면 동작합니다.
- 인스턴스의 모든 컴포넌트를 `SerializedObject`로 순회하여(리플렉션 미사용), `ScriptableObject` 참조 프로퍼티를 `Object.Instantiate`한 복사본으로 교체합니다.

> **경고**
>
> `Object.Instantiate`로 생성된 ScriptableObject는 인메모리 인스턴스이며 에셋으로 영속화되지 않습니다. 도메인 리로드나 씬 저장 이후의 보존은 보장되지 않습니다.

---

# 워크플로

1. WorldBuilder 창(`WorldBuilder > Open`)에서 **Prefab Brush Pro++**를 선택합니다.
2. **Prefabs** 목록에 프리팹을 추가하고 가중치를 조정합니다.
3. **Radius**, **Density**, 배치 옵션(Align To Normal, Random Yaw, Scale Range)을 설정합니다.
4. 필요 시 **Brush Mask**와 **Modifier Graph**를 설정합니다.
5. Scene View에서 좌클릭으로 배치합니다. **Erase Mode**를 켜면 지우기로 동작합니다.
6. **Replay / Export / Import**로 결과를 재현하거나 공유합니다.

---

# Inspector 레퍼런스

| 항목 | 타입 | 설명 |
|------|------|------|
| Seed | IntegerField | 브러시 전역 시드 |
| Randomize | Button | 새 무작위 시드 생성 |
| Radius | Slider (0.1–50) | 브러시 반경 |
| Density | SliderInt (1–50) | 스트로크당 인스턴스 수 |
| Erase Mode | Toggle | 지우기 모드 |
| Align To Normal | Toggle | 표면 법선 정렬 |
| Random Yaw | Toggle | 무작위 Y축 회전 |
| Scale Range | Vector2Field | 균등 스케일 최소/최대 |
| Chunk Size | FloatField | 바이옴 청크 좌표 변환 크기 |
| Duplicate Shared SO | Toggle | 공유 SO 독립 복사 |
| Prefabs | List | 프리팹/가중치 항목 |
| Brush Mask | Foldout | 고도/경사/바이옴 마스크 |
| Modifier Graph | ObjectField | 연결할 [Modifier Graph](ModifierGraph.md) |
| Scatter Strokes | Buttons | Replay / Export / Import / Clear |

---

# 스크립팅 레퍼런스

자세한 API는 [ScriptingAPI.md](ScriptingAPI.md)를 참고하세요. 핵심 진입점은 다음과 같습니다.

```csharp
using WorldBuilder.Editor;
using WorldBuilder.Editor.PrefabBrush;

IChunkBiomeMap chunkMap = new ChunkBiomeMap();
IBiomeMap biomeMap = new ChunkBiomeMapAdapter(chunkMap);

PrefabBrushTool tool = new PrefabBrushTool(biomeMap);
WorldBuilderToolRegistry.Register(tool);
```

---

# 관련 문서

- [Modifier Graph](ModifierGraph.md)
- [Scripting API](ScriptingAPI.md)
- [Tool System](ToolSystem.md)

# Runtime Editing

이 문서는 WorldBuilder 0.2.0에 추가된 런타임 월드 편집 기반(Roadmap: Runtime Editing)을 설명합니다.

---

# 개요

런타임에서 월드를 편집하는 경로는 두 가지입니다.

| 경로 | 어셈블리 | 용도 |
|------|----------|------|
| `RuntimePlacementService` | `WorldBuilder.Runtime` | GameObject 기반 구조물 배치/제거 |
| `WorldEntityRuntimeEditing` | `WorldBuilder.Entities` | DOTS 엔티티 스폰(기존 커맨드 큐 위 얇은 파사드) |

두 경로는 독립적이며, 게임 코드는 필요한 쪽만 참조하면 됩니다.

---

# RuntimePlacementService

정적 서비스로, 배치된 오브젝트의 장부(bookkeeping)를 관리합니다.

```csharp
using WorldBuilder.Runtime.Editing;

// 배치
var record = RuntimePlacementService.Place(prefab, position, rotation, uniformScale: 1f);

// 가장 가까운 배치 제거 (반경 제한)
if (RuntimePlacementService.RemoveNearest(hitPosition, 2f, out var removed))
{
    Debug.Log($"removed {removed.PlacementId}");
}

// 전체 초기화 (씬 언로드 등)
RuntimePlacementService.Reset();
```

특징:

- 모든 인스턴스는 `__WorldBuilder_RuntimeEdits` 루트 아래 생성됩니다.
- 각 배치는 증가하는 `PlacementId`와 프리팹 식별자(`PrefabId`)를 가집니다.
- `Records`로 현재 배치 목록을 조회할 수 있습니다.
- `Placed` / `Removed` 이벤트로 UI·사운드 등을 구독할 수 있습니다.

---

# 저장 / 복원 (0.3.0)

배치 내역을 JSON으로 직렬화하여 세이브 데이터에 포함할 수 있습니다.

```csharp
// 저장
string json = RuntimePlacementService.ToJson();

// 복원 — prefabId를 프리팹으로 되돌리는 resolver 필요
int restored = RuntimePlacementService.RestoreFromJson(json, id =>
{
    return id switch
    {
        "Hut" => hutPrefab,
        "Dock" => dockPrefab,
        _ => null   // 알 수 없는 id는 건너뜀
    };
});
```

- 위치·회전·균등 스케일이 저장됩니다.
- resolver가 null을 반환하면 해당 항목은 건너뛰고 나머지를 복원합니다.

---

# 이동 편집 (0.3.0)

`RuntimeWorldEditor.EditMode.Move`로 배치된 구조물을 표면을 따라 옮길 수 있습니다.

```csharp
editor.Mode = RuntimeWorldEditor.EditMode.Move;

if (Input.GetMouseButtonDown(0)) editor.TryGrabAtScreenPoint(Input.mousePosition);
else if (Input.GetMouseButton(0)) editor.UpdateGrab();
else editor.ReleaseGrab();
```

- 잡은 인스턴스는 카메라 중심 레이캐스트 지점(그리드 스냅/노멀 정렬 적용)을 따라갑니다.
- `RuntimePlacementService.TryGetInstanceRecord(go, out record)`로
  자식 오브젝트에서도 소유 배치를 찾을 수 있습니다.

---

# RuntimeWorldEditor

카메라 레이캐스트 기반의 최소 런타임 에디터 컴포넌트입니다.

```csharp
var editor = gameObject.AddComponent<RuntimeWorldEditor>();
editor.Mode = RuntimeWorldEditor.EditMode.Place;
editor.UpdateEditing();          // 매 프레임 호출 — 고스트 프리뷰 갱신
editor.TryPlaceAtScreenPoint(Input.mousePosition);   // 클릭 지점 배치
editor.TryRemoveAtPosition(hitPoint);               // 근처 배치 제거
```

주요 옵션:

- `placeablePrefabs`: 배치 가능한 프리팹 목록과 `SelectedPrefabIndex`
- `alignToNormal`, `snapToGrid`/`gridSize`
- `surfaceMask`, `maxPlacementDistance`, `removeRadius`
- 고스트 프리뷰는 자동으로 반투명 처리되며 콜라이더가 비활성화됩니다.

엔티티 시스템 연동이 필요하면 `Placed` 이벤트에서
`WorldEntityRuntimeEditing.TryPlace(prefabId, ...)`를 호출하는 방식으로 확장합니다.

---

# WorldEntityRuntimeEditing

기존 `WorldEntityCommandQueue`를 감싸는 런타임 편집용 파사드입니다.

```csharp
using WorldBuilder.Entities;

if (WorldEntityRuntimeEditing.IsAvailable)
    WorldEntityRuntimeEditing.TryPlace(prefabId, position, rotation, scale);

// 저장된 스냅샷 복원(세이브 데이터)도 지원
WorldEntityRuntimeEditing.TryRestore(snapshot);
```

실제 엔티티 생성은 기존 `WorldEntitySpawnSystem`이 처리합니다.

---

# 엔티티 배치 자동 미러링 (0.4.0)

`WorldEntityPlacementBridge` 컴포넌트를 씬에 두면
`RuntimePlacementService`의 GameObject 배치가 DOTS 엔티티 스폰으로 자동 미러링됩니다.

- 인스턴스 이름 → entity prefabId 바인딩 목록을 인스펙터에서 관리
- 바인딩된 이름의 배치만 `WorldEntityCommandQueue.TrySpawn`으로 전달
- 제거 이벤트는 리전 스트리밍/수명 시스템이 처리하므로 브리지는 전달하지 않음

---

# WorldData 런타임 로더 (0.4.0)

에디터에서 배치한 월드 데이터(POI·루트 컨테이너 등)를 플레이 중 소비합니다.

1. 에디터: `Tools > WorldBuilder > Export World Data Snapshot` → `WorldDataSnapshot` 에셋 생성
2. 씬에 `WorldDataRuntimeLoader` 추가 후 스냅샷 지정
3. kind→프리팹 바인딩 등록(인스펙터 또는 `AddKindBinding`)
4. 실행 시 기록별로 `RecordLoaded` 이벤트 발생 + 바인딩 프리팹 인스턴스화

```csharp
loader.RecordLoaded += record =>
{
    if (record.kind == "POI") mapMarkerManager.SpawnAt(record.position);
};
```

---

# 테스트

`Tests/EditMode/RuntimeEditingTests.cs`에서 배치/제거/JSON 왕복/로더 동작을 검증합니다.

# Water Flow & Ecology 저작 가이드

> 0.10.0 ~ 0.12.1에서 추가된 물 흐름·동굴 생태·저장 시스템 실전 가이드.

## 1. 물 흐름 파이프라인

### 1.1 바다 기본 해류

`OceanWaterBody`에 흐름을 지정하면 수면 아래 **모든** 바다 샘플이 해당 방향/속도를 받습니다.

```
OceanWaterBody
 ├─ SeaLevel          : 해수면 Y
 ├─ BaseFlowDirection : 정규화되어 저장됨 (0,0,0 가능)
 └─ BaseFlowSpeed     : m/s
```

### 1.2 WaterCurrentZone — 흐름 오버라이드

커런트 존은 **물을 만들지 않고**, 우선순위에서 이긴 물의 흐름만 교체합니다.
따라서 바다·호수·강 어디에나 얹을 수 있습니다.

```
WaterCurrentZone
 ├─ Size      : 존 크기(로컬), 트랜스폼 스케일 반영
 ├─ Direction : 교체할 흐름 방향 (예: 아래 = 폭포 하강 기류)
 ├─ Strength  : m/s
 └─ Priority  : 겹치는 존 간 승자 결정 (높을수록 우선)
```

- 베이크: `WaterBaker.Bake(bodies, settings, currentZones)`로 `CurrentZoneData`에 반영되며,
  셀 인덱스와 결정론 해시에 포함됩니다.
- 시리얼(`WaterQueryService`)과 Burst(`NativeWaterQuery`)가 동일 의미론을 유지하며
  패리티 테스트가 이를 검증합니다.

### 1.3 WaterDrifter — 표류하는 오브젝트

```csharp
var drifter = gameObject.AddComponent<WaterDrifter>();
drifter.QueryService = new WaterQueryService(waterData); // 부트스트랩에서 주입
drifter.Parameters.FloatOnSurface = true;   // false면 계속 가라앉음
drifter.Parameters.FloatDraft    = 0.35f;   // 원점-수면 거리
```

- `WaterDrift.Integrate(position, velocity, sample, parameters, dt)`는 순수 함수라
  플레이어 수영·DOTS 커스텀 통합 등 어디든 재사용할 수 있습니다.
- 항력은 **흐름과의 편차에만** 작용하므로 강류 속도가 항상 우세합니다(평형 = 유속).

### 1.4 GroundwaterService — 지하수대

```csharp
IWaterQueryService water = new GroundwaterService(baseService, waterTableY: -12f);
EnvironmentClassifier.Classify(water, sampler, position); // 밀폐 + 테이블 하부 = FloodedCave
```

`CaveShapeParams.waterTableY`를 설정해두면 저작 단계에서도 의도 침수선을 문서화할 수 있습니다.

### 1.5 WaterLevelDriver — 날씨→수위 연동

```csharp
driver.Target     = waterData;
driver.SetIntensity(1f);   // 0=가뭄, 0.5=보통, 1=홍수
// → 해수면 -maxDrop..+maxRise, 유속 ×speedMultiplierRange 적용
```

- 오프셋은 런타임 전용(베이크 데이터 불변). 비/눈 상태 머신에서 `SetIntensity`만 호출하세요.
- 주의: 오프셋 변경 후에는 `NativeWaterQuery`를 다시 생성해야 Burst 경로도 반영됩니다.

## 2. 동굴 저작 워크플로

1. **Terrain Forge → Generate** (Shape 프리셋 선택)
2. **⑥ Caves**: Cave Shape Params 할당/생성 → 프리셋 클릭 → Generate에 카빙 포함 또는
   *Carve Caves Only*
3. **입구 관통**: `CaveField.CarveEntrances(store, heights, shape, caves, chunkSize, count)` —
   표면에서 공기 주머니까지 보행 샤프트를 결정론적으로 뚫습니다
4. **블렌더 경로(선택)**: Blender Cave Network Builder 산출물을 임포트 후
   *WorldBuilder/Caves/Carve Store With Selected Mesh* → 복셀이 그대로 파이며 등록 청크는 즉시 리메시
5. **동굴 생태 배치**: *WorldBuilder/PCG/Create Rule Set/Cave Interior Ecology*로 규칙 세트 생성 →
   광막/형광 이끼 프리팹 할당 → Terrain Forge *Scatter Cave Interior* 버튼으로 베이크
6. **검증**: *WorldBuilder/Audit/Run Terrain Checks* — 고아 청크/NaN 밀도/경계 불일치 점검

## 3. 통합 세이브 v2

```csharp
WorldSaveService.SaveSnapshot(slot, store,
    TerrainDeformer.EditedChunks,       // 지형 델타
    RuntimePlacementService.ToJson(),   // 배치 스냅샷
    extrasJson: "{\"weather\":\"rain\"}");

WorldSaveService.LoadSnapshot(slot, store, prefabResolver, out string extrasJson);
```

- 배치/지형/extras가 한 번에 저장·복원되며, 기존 단독 슬롯과 하위 호환입니다.

## 4. 체크리스트

- [ ] 커런트 존 겹침 구역에서 우선순위 의도 확인 (Priority)
- [ ] `NativeWaterQuery` 재사용 시 수위 오프셋 반영을 위해 재생성했는지
- [ ] 동굴 입구 주변 surfaceProtectDepth 충족(입구가 막히면 두께 감소)
- [ ] Cave Interior 생태 배치 전 Audit으로 경계 불일치 제거

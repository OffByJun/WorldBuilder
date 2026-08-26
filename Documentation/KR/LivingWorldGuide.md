# Living World 가이드 (0.15.0+)

> 낮밤 · 날씨 · 물 순환 · 성장/채집 · 오토세이브를 한 사이클로 묶는 방법.

## 1. 씬 구성 (부트스트랩)

```
WorldClock                 (필수 — 시간 원천)
DayNightAtmosphere         태양 색/밤 틴트 (SeasonPalette 옵션)
SimpleWeatherController    Clear/Overcast/Fog/Rain 자동 전환
PrecipitationFx            비 파티클 + 젖음 + 수위 연동
EnvironmentFxRig           도메인별 포그·볼륨·환경음·믹서 스냅샷
WaterLevelDriver           날씨 → 해수면/유속
SnowCoverageDriver         겨울 적설 (_WB_Snow)
RiverbedFlowSim            강 침식/퇴적
CollapseWatcher            붕괴 감지
AutoSaveService            회전 오토세이브
```

모든 컴포넌트는 서로를 직접 참조하지 않고 RenderSettings/Shader 글로벌/레지스트리로 느슨히 결합됩니다.

## 2. 시간 → 계절 → 낮밤

```csharp
// 게임 코드에서 하루가 지날 때마다:
SeasonState.CurrentSeason = (SeasonState.CurrentSeason + 1) % 4;
dayNightAtmosphere.SetSeason(SeasonState.CurrentSeason);
```

- `DayNightAtmosphere`는 `WorldClock.Nightness`로 포그/앰비언트를 밤 틴트로 승수합니다.
  다른 시스템(SimpleWeatherController, EnvironmentFxRig)이 쓴 절대색을 **베이스로 존중**하며,
  외부에서 값을 바꾸면 자동으로 새 베이스를 채택합니다(누적 변질 없음).
- `SnowCoverageDriver`의 autoWinter가 켜져 있으면 겨울에 `_WB_Snow`가 상승해
  TerrainSplat 위쪽 면이 하얗게 블렌딩됩니다.

## 3. 날씨 → 젖음 → 강 불어남

`PrecipitationFx` 하나면 닫힙니다:

1. `SimpleWeatherController`가 Rain으로 전환 → 파티클 재생
2. `_WB_Wetness` 글로벌 상승 → TerrainSplat이 어두워지고 매끈해짐
3. `WaterLevelDriver.SetIntensity(stormIntensity)` → 해수면 상승 + 유속 배수
4. `RiverbedFlowSim`은 유효 유속을 사용하므로 홍수 때 침식이 빨라집니다

## 4. 성장 · 채집

```csharp
var tree = Instantiate(treePrefab);          // GrowableResource + HarvestableNode 포함
tree.GetComponent<GrowableResource>().Advance(60f); // 또는 Update에 맡김

if (node.ReadyForHarvest && node.TryHarvest(out var rolled))
    inventory.Add(rolled);                    // itemId/min/max 롤 결과
// 수확 후 자동으로 stage 0 리스폰 (destroyOnHarvest=false인 경우)
```

- 모드 JSON(`growthSecondsPerStage`, `harvestYields`)으로 전체 기본값 교체 가능 → ContentModLoader 참조.
- PCG `growthStages` 프리팹과 조합하면 scatter된 노드가 새싹부터 다시 자랍니다.

## 5. 낚시

```csharp
FishingSession session = fishingSpot.BeginCast(bobber.position, rngSeed);
// 매 프레임
session.Tick(Time.deltaTime);
if (session.Phase == FishingPhase.Biting && input.reeled)
    if (session.TryReel(out string fishId)) inventory.Add(fishId);
```

어종 테이블은 깊이 밴드 게이트가 있으므로 심수 어종은 깊은 곳에서만 잡힙니다.
역시 모드 JSON으로 교체 가능합니다.

## 6. 수중 생존

```csharp
breather.DrowningTick += () => health.Damage(2f);
breather.AirChanged += ratio => ui.airBar.fillAmount = ratio;
```

`gilled=true`면 물속에서도 공기가 유지되어 물고기가 익사하지 않습니다.
패닉 스윔 어시스트는 공기가 부족해지면 WaterDrifter를 띄워 올립니다.

## 7. 오토세이브

```csharp
autoSave.Bind(
    () => voxelStore,
    () => TerrainDeformer.EditedChunks,
    () => RuntimePlacementService.ToJson(),
    prefabResolver);
autoSave.TickNow();   // 즉시 저장도 가능
```

- `autosave_00..NN` 링 회전, 보관 수 초과 시 가장 오래된 슬롯 삭제.
- 세이브에는 지형 델타+배치+extras가 함께 들어갑니다(SaveSnapshot v2).

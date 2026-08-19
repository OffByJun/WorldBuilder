# DOTS Resources and Dropped Items

## Resource categories

- Ground pickups use a `DroppedItem` entity and transfer only the amount accepted by the existing inventory.
- Tool nodes use `ResourceNode` with an exact item id, method, minimum tier, and minimum power requirement.
- Hand-hit resources use `HarvestMethod.Hand`; trees can combine `Hand | Axe` and let axes deal more damage.

## Authoring

1. Create a generic dropped-item entity prefab with `WorldEntityAuthoring`, `DroppedItemAuthoring`, Entities Graphics rendering, and Unity Physics collider authoring.
2. Register that prefab id in `WorldEntityRuntimeAuthoring`.
3. Create node prefabs with `WorldEntityAuthoring`, `ResourceNodeAuthoring`, rendering, and Unity Physics collider authoring.
4. Configure drop item ids/count ranges/probability on each node.
5. Use `ResourceFieldSpawnZoneAuthoring` in a SubScene to maintain either resource nodes or loose ground pickups.

Field zones raycast through the DOTS `PhysicsWorldSingleton`. Ground that should receive spawns must therefore be baked with Unity Physics collision. A zone tracks its own spawned entities in a dynamic buffer and never scans every world entity.

## Game integration

`InteractionHandler` performs a DOTS Physics raycast when no regular GameObject interactable was hit. `HarvestToolCatalog` maps existing inventory item ids to `Hand`, `Axe`, `Pickaxe`, or `Drill`, plus tier, power, and damage.

`DotsResourceInventoryBridge` processes ECS inventory grant requests through the existing `IInventoryService`. If only part of a stack fits, only that amount is removed from the dropped entity; the remainder stays in the world.

## Runtime flow

```text
Interaction ray
  -> ResourceHarvestRequest / DroppedItemPickupRequest
  -> ECS validation and state update
  -> deterministic ResourceDropSpawnRequest
  -> ECS dropped-item entity
  -> InventoryGrantRequest
  -> IInventoryService.AddItem
  -> InventoryGrantResult
  -> decrement or destroy dropped entity
```

Depleted nodes with a respawn time receive `Disabled`, which removes them from normal rendering, physics, and simulation queries. `ResourceRespawnSystem` restores health and removes `Disabled` when the timer ends. Persistent node health, remaining respawn time, item id, and remaining stack count are included in entity snapshots.

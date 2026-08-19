"""Stable scatter instance state and rebuild merge policy."""

import math

GENERATED = "GENERATED"
MANUALLY_MOVED = "MANUALLY_MOVED"
EXCLUDED = "EXCLUDED"
LOCKED = "LOCKED"


def instance_id(layer_id, sample_key):
    from .scatter_rules import stable_seed
    return f"{stable_seed('instance', layer_id, sample_key):016x}"


def transform_changed(current, generated, position_epsilon=1e-4, rotation_epsilon=1e-4, scale_epsilon=1e-4):
    tolerances = (position_epsilon, rotation_epsilon, scale_epsilon)
    for values, baseline, tolerance in zip(current, generated, tolerances):
        if len(values) != len(baseline):
            return True
        if any(abs(float(a) - float(b)) > tolerance for a, b in zip(values, baseline)):
            return True
    return False


def merge_rebuild(existing, generated, preserve_manual=True, preserve_deleted=True, remove_orphans=False):
    """Return stable merged records keyed by instance_id."""
    old = {record["instance_id"]: dict(record) for record in existing}
    new = {record["instance_id"]: dict(record) for record in generated}
    merged = []
    for key in sorted(new):
        previous = old.get(key)
        if previous and previous.get("state") == EXCLUDED and preserve_deleted:
            continue
        if previous and previous.get("state") in {MANUALLY_MOVED, LOCKED} and preserve_manual:
            merged.append(previous)
        else:
            merged.append(new[key])
    if not remove_orphans:
        for key in sorted(set(old) - set(new)):
            if old[key].get("state") in {MANUALLY_MOVED, LOCKED}:
                orphan = dict(old[key])
                orphan["orphan"] = True
                merged.append(orphan)
    return merged


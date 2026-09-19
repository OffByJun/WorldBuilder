import math
import re
from pathlib import Path


MIN_BONE_LENGTH = 1e-5


def validate_point(point):
    try:
        result = tuple(float(component) for component in point)
    except (TypeError, ValueError, OverflowError) as error:
        raise ValueError("Joint positions must be finite XYZ points") from error
    if len(result) != 3 or not all(math.isfinite(component) for component in result):
        raise ValueError("Joint positions must be finite XYZ points")
    return result


def bone_plan(names, parents, points):
    names = [str(name).strip() for name in names]
    parents = list(parents)
    points = [validate_point(point) for point in points]
    count = len(names)
    if count < 2 or len(parents) != count or len(points) != count:
        raise ValueError("Place at least two joints with matching parents and positions")
    if any(not name for name in names) or len(set(names)) != count:
        raise ValueError("Joint names must be nonempty and unique")
    for index, parent in enumerate(parents):
        if type(parent) is not int or parent < -1 or parent >= count:
            raise ValueError(f"Missing parent for joint {index}")
        if parent == index:
            raise ValueError(f"Joint {index} cannot parent itself (cycle)")
    complete = set()
    order = []
    for index in range(count):
        path = []
        visiting = set()
        current = index
        while current != -1 and current not in complete:
            if current in visiting:
                raise ValueError("Joint parents contain a cycle")
            visiting.add(current)
            path.append(current)
            current = parents[current]
        for current in reversed(path):
            complete.add(current)
            order.append(current)
    if parents.count(-1) != 1:
        raise ValueError("Choose exactly one root joint (parent -1)")
    if len(set(points)) != count:
        raise ValueError("Duplicate joint positions create zero-length or overlapping joints")
    bone_names = {
        index: f"WB_{index:03d}_{re.sub(r'[^A-Za-z0-9_]+', '_', name).strip('_')[:40] or 'Joint'}"
        for index, name in enumerate(names) if parents[index] != -1
    }
    result = []
    for index in order:
        parent = parents[index]
        if parent == -1:
            continue
        length = math.dist(points[parent], points[index])
        if not math.isfinite(length) or length <= MIN_BONE_LENGTH:
            raise ValueError(f"Joint {index} has a zero-length or invalid bone")
        result.append({
            "joint_index": index,
            "name": bone_names[index],
            "parent": bone_names.get(parent),
            "head": points[parent],
            "tail": points[index],
        })
    return result


def parents_after_removal(parents, index):
    parents = list(parents)
    if not 0 <= index < len(parents):
        raise ValueError("Select a joint")
    if any(parent == index for parent in parents):
        raise ValueError("Reparent or delete child joints first")
    return [parent - 1 if parent > index else parent
            for current, parent in enumerate(parents) if current != index]


def validate_weights(vertices, bone_names):
    bone_names = set(bone_names)
    if not bone_names:
        raise ValueError("Armature has no deform bones")
    used = set()
    count = 0
    for index, assignments in enumerate(vertices):
        count += 1
        total = 0.0
        for name, weight in assignments:
            if name not in bone_names:
                continue
            if not math.isfinite(weight) or weight < 0.0 or weight > 1.0:
                raise ValueError(f"Invalid automatic weight on vertex {index}")
            if weight > 1e-8:
                used.add(name)
                total += weight
        if total <= 1e-8:
            raise ValueError(f"Automatic weights failed: vertex {index} is unweighted")
    if not count:
        raise ValueError("Target mesh has no vertices")
    if used != bone_names:
        raise ValueError("Automatic weights failed: some bones have no weighted vertices")


def validate_export_path(filepath):
    if not str(filepath).strip():
        raise ValueError("Choose an FBX file")
    path = Path(filepath).expanduser().resolve()
    if path.suffix.lower() != ".fbx" or not path.parent.is_dir() or path.is_dir():
        raise ValueError("Choose an FBX file in an existing folder")
    if any(entry.name.lower().endswith(".chunk.json") for entry in path.parent.iterdir()):
        raise ValueError("Choose a separate asset folder, not a chunk manifest folder")
    return str(path)

import sys

sys.dont_write_bytecode = True

import importlib.util
import math
import os
from pathlib import Path
import unittest
from unittest.mock import patch
import uuid

import bpy
from _bpy_restrict_state import RestrictBlend
from mathutils import Vector


ADDON_ROOT = Path(__file__).resolve().parents[1]
OUTPUT = Path(os.environ["WB_SMOKE_OUTPUT"]).resolve()
PREFIX = f"wb_easy_rigging_{uuid.uuid4().hex}"
EPSILON = 1e-4


def load_addon():
    name = "wb_easy_rigging_smoke_addon"
    spec = importlib.util.spec_from_file_location(
        name, ADDON_ROOT / "__init__.py",
        submodule_search_locations=[str(ADDON_ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def object_mode():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")


def select(objects, active, mode="OBJECT"):
    object_mode()
    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active
    if mode != "OBJECT":
        bpy.ops.object.mode_set(mode=mode)


def context_state():
    active = bpy.context.view_layer.objects.active
    return (
        tuple(sorted(obj.name for obj in bpy.context.view_layer.objects if obj.select_get())),
        active.name if active else None,
        bpy.context.mode,
        active.mode if active else "OBJECT",
    )


def matrix_values(matrix):
    return tuple(component for row in matrix for component in row)


def weight_state(mesh):
    names = {group.index: group.name for group in mesh.vertex_groups}
    return (
        tuple((group.name, group.lock_weight) for group in mesh.vertex_groups),
        tuple(tuple((names[item.group], item.weight) for item in vertex.groups)
              for vertex in mesh.data.vertices),
    )


def binding_state(mesh):
    return {
        "weights": weight_state(mesh),
        "parent": mesh.parent.name if mesh.parent else None,
        "parent_type": mesh.parent_type,
        "parent_bone": mesh.parent_bone,
        "inverse": matrix_values(mesh.matrix_parent_inverse),
        "basis": matrix_values(mesh.matrix_basis),
        "world": matrix_values(mesh.matrix_world),
        "modifiers": tuple((mod.as_pointer(), mod.name, mod.type,
                            mod.show_viewport, mod.show_render)
                           for mod in mesh.modifiers),
        "data": mesh.data.as_pointer(),
    }


def evaluated_points(mesh):
    bpy.context.view_layer.update()
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = evaluated.to_mesh()
    try:
        return [evaluated.matrix_world @ vertex.co for vertex in data.vertices]
    finally:
        evaluated.to_mesh_clear()


def bounds(points):
    return tuple(function(point[axis] for point in points)
                 for function in (min, max) for axis in range(3))


def guide_state(value):
    return {
        "owner": value.owner_id,
        "target": value.target.name,
        "rig": value.armature.name,
        "rig_target": value.rig_target.name,
        "active": value.active_index,
        "joints": tuple((item.name, item.parent_index, item.parent_name,
                         item.guide.name, matrix_values(item.guide.matrix_world),
                         item.guide.get("wb_easy_rig_owner"),
                         item.guide.get("wb_easy_rig_kind"))
                        for item in value.joints),
    }


class EasyRiggingSmoke(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.addon = load_addon()
        cls.easy = cls.addon.worldbuilder_chunks.easy_rigging
        if cls.easy not in cls.addon.worldbuilder_chunks._feature_modules:
            raise AssertionError("Easy Rigging is missing from the main registration lifecycle")
        with RestrictBlend():
            cls.addon.register()
        cls.addClassCleanup(cls.addon.unregister)

    def setUp(self):
        object_mode()
        self.scene = bpy.data.scenes.new(f"{PREFIX}_{self._testMethodName}")
        bpy.context.window.scene = self.scene
        self.sentinel = bpy.data.objects.new("UnrelatedSelection", None)
        self.scene.collection.objects.link(self.sentinel)

    def tearDown(self):
        object_mode()
        scene = bpy.context.scene
        objects = list(scene.objects)
        bpy.context.window.scene = next(item for item in bpy.data.scenes if item != scene)
        bpy.data.scenes.remove(scene)
        for obj in objects:
            if not obj.users_scene:
                bpy.data.objects.remove(obj, do_unlink=True)

    def assert_close(self, actual, expected, tolerance=EPSILON):
        self.assertEqual(len(actual), len(expected))
        for index, (left, right) in enumerate(zip(actual, expected)):
            self.assertAlmostEqual(left, right, delta=tolerance, msg=f"component {index}")

    def make_guides(self, shape="SPHERE", branch=False):
        if shape == "CYLINDER":
            self.assertEqual(bpy.ops.mesh.primitive_cylinder_add(
                vertices=32, radius=0.8, depth=4.0), {"FINISHED"})
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            self.assertEqual(bpy.ops.mesh.subdivide(number_cuts=5), {"FINISHED"})
            object_mode()
        else:
            self.assertEqual(bpy.ops.mesh.primitive_uv_sphere_add(
                segments=32, ring_count=24, radius=1.0), {"FINISHED"})
            for vertex in bpy.context.object.data.vertices:
                vertex.co.z *= 2.0
        mesh = bpy.context.object
        mesh.name = f"Smoke_{shape}"
        mesh.location = (2.5, -1.75, 0.6)
        mesh.rotation_euler = (0.17, -0.23, 0.31)
        bpy.context.view_layer.update()
        value = self.easy.settings(bpy.context.scene)
        value.target = mesh
        points = [(0.0, 0.0, -1.3), (0.0, 0.0, -0.3), (0.0, 0.0, 1.2)]
        parents = [-1, 0, 1]
        if branch:
            points = [(0.0, 0.0, -1.3), (0.0, 0.0, -0.3),
                      (-0.35, 0.0, 0.9), (0.35, 0.0, 0.9)]
            parents = [-1, 0, 1, 1]
        for index, (point, parent) in enumerate(zip(points, parents)):
            self.assertLess((point[0] / 0.8) ** 2 + point[1] ** 2 + (point[2] / 2) ** 2, 0.8)
            value.active_index = max(0, parent)
            bpy.context.scene.cursor.location = mesh.matrix_world @ Vector(point)
            self.assertEqual(bpy.ops.worldbuilder.easy_rig_add_joint(), {"FINISHED"})
            item = value.joints[index]
            item.name = f"SmokeJoint_{index}"
            self.assertEqual(item.parent_index, parent)
            self.assertEqual(item.guide.type, "EMPTY")
            self.assertEqual(item.guide.get(self.easy.OWNER_KEY), value.owner_id)
            self.assertEqual(item.guide.get(self.easy.KIND_KEY), "GUIDE")
            self.assertEqual(item.guide.worldbuilder_chunk.role, "GLOBAL")
        bpy.context.view_layer.update()
        return mesh, value

    def build(self, shape="SPHERE", branch=False):
        mesh, value = self.make_guides(shape, branch)
        select([mesh, self.sentinel], mesh, "EDIT")
        before = context_state()
        self.assertEqual(bpy.ops.worldbuilder.easy_rig_build(), {"FINISHED"})
        self.assertEqual(context_state(), before)
        rig = value.armature
        plan = self.easy.core.bone_plan(
            [item.name for item in value.joints],
            [item.parent_index for item in value.joints],
            [item.guide.matrix_world.translation for item in value.joints],
        )
        self.assertEqual(len(rig.data.bones), len(plan))
        self.assertEqual(value.rig_target, mesh)
        self.assertEqual(rig.get(self.easy.OWNER_KEY), value.owner_id)
        for edge in plan:
            bone = rig.data.bones[edge["name"]]
            self.assertEqual(bone.parent.name if bone.parent else None, edge["parent"])
            self.assertEqual(bone.use_connect, edge["parent"] is not None)
            self.assertTrue(bone.use_deform)
            self.assert_close(rig.matrix_world @ bone.head_local, edge["head"])
            self.assert_close(rig.matrix_world @ bone.tail_local, edge["tail"])
        return mesh, rig

    def bind(self, mesh, rig):
        before = context_state()
        world = matrix_values(mesh.matrix_world)
        self.assertEqual(bpy.ops.worldbuilder.easy_rig_bind(), {"FINISHED"})
        self.assertEqual(context_state(), before)
        self.assert_close(matrix_values(mesh.matrix_world), world)
        self.assertEqual(mesh.parent, rig)
        modifiers = [mod for mod in mesh.modifiers if mod.type == "ARMATURE"]
        self.assertEqual(len(modifiers), 1)
        self.assertEqual(modifiers[0].object, rig)
        self.easy.inspect_weights(mesh, rig)

    def assert_deformation(self, mesh, rig):
        object_mode()
        rest = evaluated_points(mesh)
        self.assertEqual(bpy.ops.worldbuilder.easy_rig_pose(), {"FINISHED"})
        self.assertEqual(context_state(), ((rig.name,), rig.name, "POSE", "POSE"))
        bone = next(bone for bone in rig.pose.bones if bone.parent is not None)
        bone.rotation_mode = "XYZ"
        bone.rotation_euler.x = math.radians(32)
        posed = evaluated_points(mesh)
        self.assertEqual(len(rest), len(posed))
        distances = [(left - right).length for left, right in zip(rest, posed)]
        self.assertGreater(max(distances), 0.15, "Pose did not deform evaluated mesh")
        self.assertGreater(sum(distance > 0.01 for distance in distances), len(rest) // 10)
        bone.matrix_basis.identity()
        restored = evaluated_points(mesh)
        self.assertLess(max((left - right).length for left, right in zip(rest, restored)), EPSILON)

    def test_registration_lifecycle(self):
        self.assertTrue(hasattr(bpy.types.Scene, "worldbuilder_easy_rigging"))
        for cls in self.easy.CLASSES:
            self.assertTrue(cls.is_registered, cls.__name__)
        with RestrictBlend():
            self.addon.unregister()
        self.assertFalse(hasattr(bpy.types.Scene, "worldbuilder_easy_rigging"))
        for cls in self.easy.CLASSES:
            self.assertFalse(cls.is_registered, cls.__name__)
        with RestrictBlend():
            self.addon.register()
        self.assertIsNotNone(self.easy.settings(bpy.context.scene))
        self.assertEqual(len(self.easy._registered), len(self.easy.CLASSES))

    def test_chain_bind_pose_and_duplicate_refusal(self):
        mesh, rig = self.build()
        self.bind(mesh, rig)
        object_mode()
        select([mesh, self.sentinel], mesh, "EDIT")
        before_context = context_state()
        before_binding = binding_state(mesh)
        with self.assertRaisesRegex(ValueError, "Existing groups, weights, parents and rigs are preserved"):
            self.easy.bind_automatic(bpy.context)
        self.assertEqual(context_state(), before_context)
        self.assertEqual(binding_state(mesh), before_binding)
        self.assert_deformation(mesh, rig)

    def test_cylinder_chain(self):
        mesh, rig = self.build("CYLINDER")
        self.bind(mesh, rig)
        self.assert_deformation(mesh, rig)

    def test_branch_bind_pose(self):
        mesh, rig = self.build(branch=True)
        self.assertEqual(sorted(len(bone.children) for bone in rig.data.bones), [0, 0, 2])
        self.bind(mesh, rig)
        self.assert_deformation(mesh, rig)

    def test_bind_failure_rolls_back(self):
        mesh, rig = self.build()
        object_mode()
        modifier = mesh.modifiers.new("PreservedTriangulate", "TRIANGULATE")
        modifier.show_viewport = False
        modifier.show_render = False
        select([mesh, self.sentinel], mesh, "EDIT")
        before_context = context_state()
        before_binding = binding_state(mesh)
        objects = set(bpy.data.objects.keys())
        inspect_weights = self.easy.inspect_weights

        def fail_after_real_binding(target, armature):
            self.assertEqual(target.parent, armature)
            self.assertTrue(any(mod.type == "ARMATURE" and mod.object == armature
                                for mod in target.modifiers))
            inspect_weights(target, armature)
            raise RuntimeError("WB_SMOKE_INJECTED_INSPECTION_FAILURE")

        with patch.object(self.easy, "inspect_weights", side_effect=fail_after_real_binding) as mocked:
            with self.assertRaisesRegex(RuntimeError, "^WB_SMOKE_INJECTED_INSPECTION_FAILURE$"):
                self.easy.bind_automatic(bpy.context)
            mocked.assert_called_once_with(mesh, rig)
        self.assertEqual(context_state(), before_context)
        after_binding = binding_state(mesh)
        for key in ("inverse", "basis", "world"):
            self.assert_close(after_binding.pop(key), before_binding.pop(key), tolerance=1e-6)
        self.assertEqual(after_binding, before_binding)
        self.assertEqual(set(bpy.data.objects.keys()), objects)
        self.bind(mesh, rig)

    def test_save_reopen_guide_persistence(self):
        mesh, rig = self.build(branch=True)
        self.bind(mesh, rig)
        object_mode()
        value = self.easy.settings(bpy.context.scene)
        value.active_index = 1
        value.joints[1].name = "Persisted Spine"
        bpy.context.view_layer.update()
        expected = guide_state(value)
        weights = weight_state(mesh)
        scene_name = bpy.context.scene.name
        path = OUTPUT / f"{PREFIX}_guides.blend"
        self.assertEqual(bpy.ops.wm.save_as_mainfile(filepath=str(path)), {"FINISHED"})
        self.assertGreater(path.stat().st_size, 0)
        value.joints[1].name = "Unsaved Mutation"
        value.joints[1].guide.location.x += 9
        value.active_index = 0
        self.assertEqual(bpy.ops.wm.open_mainfile(filepath=str(path)), {"FINISHED"})
        self.assertEqual(bpy.context.scene.name, scene_name)
        bpy.context.view_layer.update()
        value = self.easy.settings(bpy.context.scene)
        self.assertEqual(guide_state(value), expected)
        self.assertEqual(weight_state(value.target), weights)
        self.easy.inspect_weights(value.target, value.armature)
        self.assert_deformation(value.target, value.armature)

    def test_unity_fbx_roundtrip(self):
        mesh, rig = self.build()
        self.bind(mesh, rig)
        object_mode()
        rest_bounds = bounds(evaluated_points(mesh))
        bones = {bone.name: (bone.parent.name if bone.parent else None,
                             tuple(rig.matrix_world @ bone.head_local))
                 for bone in rig.data.bones}
        source_weights = weight_state(mesh)[1]
        source_points = [mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices]
        select([mesh, self.sentinel], mesh, "EDIT")
        before_context = context_state()
        path = OUTPUT / f"{PREFIX}_unity.fbx"
        self.assertEqual(bpy.ops.worldbuilder.easy_rig_export(
            filepath=str(path), bake_anim=False), {"FINISHED"})
        self.assertEqual(context_state(), before_context)
        self.assertGreater(path.stat().st_size, 0)
        object_mode()
        for obj in list(bpy.context.scene.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        self.assertEqual(bpy.ops.import_scene.fbx(filepath=str(path)), {"FINISHED"})
        imported = list(bpy.context.scene.objects)
        self.assertEqual(sorted(obj.type for obj in imported), ["ARMATURE", "MESH"])
        imported_mesh = next(obj for obj in imported if obj.type == "MESH")
        imported_rig = next(obj for obj in imported if obj.type == "ARMATURE")
        self.assertEqual(set(imported_rig.data.bones.keys()), set(bones))
        for bone in imported_rig.data.bones:
            parent, head = bones[bone.name]
            self.assertEqual(bone.parent.name if bone.parent else None, parent)
            self.assert_close(imported_rig.matrix_world @ bone.head_local, head)
        self.easy.inspect_weights(imported_mesh, imported_rig)
        modifiers = [mod for mod in imported_mesh.modifiers if mod.type == "ARMATURE"]
        self.assertEqual(len(modifiers), 1)
        self.assertEqual(modifiers[0].object, imported_rig)
        self.assert_close(bounds(evaluated_points(imported_mesh)), rest_bounds)
        imported_weights = weight_state(imported_mesh)[1]
        self.assertEqual(len(imported_mesh.data.vertices), len(source_points))
        matched = set()
        for vertex, assignments in zip(imported_mesh.data.vertices, imported_weights):
            point = imported_mesh.matrix_world @ vertex.co
            index = min(range(len(source_points)), key=lambda i: (source_points[i] - point).length_squared)
            self.assertLess((source_points[index] - point).length, EPSILON)
            self.assertNotIn(index, matched, "FBX vertex matching was not one-to-one")
            matched.add(index)
            expected = dict(source_weights[index])
            actual = dict(assignments)
            for name in set(expected) | set(actual):
                self.assertAlmostEqual(actual.get(name, 0), expected.get(name, 0), delta=1e-5)
        self.assertEqual(len(matched), len(source_points))
        value = self.easy.settings(bpy.context.scene)
        value.target = imported_mesh
        value.rig_target = imported_mesh
        value.armature = imported_rig
        imported_rig[self.easy.OWNER_KEY] = value.owner_id
        imported_rig[self.easy.KIND_KEY] = "ARMATURE"
        self.assert_deformation(imported_mesh, imported_rig)


class Tee:
    def __init__(self, *streams):
        self.streams = streams

    def write(self, text):
        for stream in self.streams:
            stream.write(text)
        return len(text)

    def flush(self):
        for stream in self.streams:
            stream.flush()


def main():
    if not OUTPUT.is_dir():
        raise RuntimeError(f"WB_SMOKE_OUTPUT must be an existing directory: {OUTPUT}")
    log = OUTPUT / f"{PREFIX}.log"
    print(f"WB_EASY_RIGGING_OUTPUT={OUTPUT / PREFIX}", flush=True)
    with log.open("w", encoding="utf-8") as stream:
        result = unittest.TextTestRunner(
            stream=Tee(sys.stdout, stream), verbosity=2,
        ).run(unittest.defaultTestLoader.loadTestsFromTestCase(EasyRiggingSmoke))
    if not result.wasSuccessful():
        raise RuntimeError(f"Easy Rigging smoke failed; full test stack traces: {log}")
    print("WB_EASY_RIGGING_SMOKE_OK", flush=True)


if __name__ == "__main__":
    main()

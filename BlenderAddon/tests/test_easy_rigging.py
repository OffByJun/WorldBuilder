import ast
import tempfile
import unittest
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace

from _load_modules import load


core = load("easy_rigging_core")


class BonePlanTests(unittest.TestCase):
    def test_chain_has_only_parent_child_edges(self):
        plan = core.bone_plan(["Root", "Elbow", "Tip"], [-1, 0, 1],
                              [(0, 0, 0), (0, 0, 1), (0, 0, 2)])
        self.assertEqual([edge["name"] for edge in plan], ["WB_001_Elbow", "WB_002_Tip"])
        self.assertIsNone(plan[0]["parent"])
        self.assertEqual(plan[1]["parent"], plan[0]["name"])
        self.assertEqual(plan[1]["head"], plan[0]["tail"])

    def test_branching_and_parent_after_child(self):
        plan = core.bone_plan(["Root", "Left", "Right", "Trunk"], [-1, 3, 3, 0],
                              [(0, 0, 0), (-1, 0, 2), (1, 0, 2), (0, 0, 1)])
        self.assertEqual([edge["joint_index"] for edge in plan], [3, 1, 2])
        self.assertEqual(plan[1]["parent"], plan[0]["name"])
        self.assertEqual(plan[2]["parent"], plan[0]["name"])

    def test_root_branches_have_no_fabricated_root_bone(self):
        plan = core.bone_plan(["Root", "Left", "Right"], [-1, 0, 0],
                              [(0, 0, 0), (-1, 0, 1), (1, 0, 1)])
        self.assertEqual(len(plan), 2)
        self.assertTrue(all(edge["parent"] is None for edge in plan))

    def test_names_are_deterministic_unique_and_fbx_safe(self):
        args = (["Root", "arm.L", "arm L", "관절" * 40], [-1, 0, 0, 0],
                [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)])
        first = core.bone_plan(*args)
        self.assertEqual(first, core.bone_plan(*args))
        self.assertEqual(len({edge["name"] for edge in first}), 3)
        for edge in first:
            self.assertLess(len(edge["name"].encode("utf-8")), 64)
            self.assertRegex(edge["name"], r"^WB_[0-9]+_[A-Za-z0-9_]+$")

    def test_invalid_graphs(self):
        cases = [
            (["Only"], [-1], [(0, 0, 0)]),
            (["Root", "Tip"], [-1], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [-1, 2], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [-1, -2], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [-1, 1], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [1, 0], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [-1, -1], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Root"], [-1, 0], [(0, 0, 0), (0, 0, 1)]),
            (["Root", " "], [-1, 0], [(0, 0, 0), (0, 0, 1)]),
            (["Root", "Tip"], [-1, 0], [(0, 0, 0), (0, 0, 0)]),
            (["Root", "Tip"], [-1, 0], [(0, 0, 0), (0, 0, 1e-8)]),
            (["Root", "Tip"], [-1, False], [(0, 0, 0), (0, 0, 1)]),
        ]
        for args in cases:
            with self.subTest(args=args), self.assertRaises(ValueError):
                core.bone_plan(*args)

    def test_disconnected_cycle_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "cycle"):
            core.bone_plan(["Root", "A", "B"], [-1, 2, 1],
                           [(0, 0, 0), (0, 0, 1), (0, 0, 2)])

    def test_duplicate_branch_positions_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            core.bone_plan(["Root", "A", "B"], [-1, 0, 0],
                           [(0, 0, 0), (0, 0, 1), (0, 0, 1)])

    def test_nonfinite_and_malformed_points(self):
        for point in [(float("nan"), 0, 0), (0, float("inf"), 0), (0, 0), None, ("x", 0, 0)]:
            with self.subTest(point=point), self.assertRaises(ValueError):
                core.bone_plan(["Root", "Tip"], [-1, 0], [(0, 0, 0), point])

    def test_deep_chain_avoids_recursion_limit(self):
        count = 1500
        plan = core.bone_plan([str(i) for i in range(count)], [-1] + list(range(count - 1)),
                              [(0, 0, i) for i in range(count)])
        self.assertEqual(len(plan), count - 1)


class JointRemovalTests(unittest.TestCase):
    def test_leaf_removal_remaps_later_parent_indices(self):
        parents = [-1, 0, 0, 2]
        self.assertEqual(core.parents_after_removal(parents, 1), [-1, 0, 1])
        self.assertEqual(parents, [-1, 0, 0, 2])

    def test_root_or_branch_with_children_is_preserved(self):
        for index in (0, 1):
            with self.subTest(index=index), self.assertRaisesRegex(ValueError, "child"):
                core.parents_after_removal([-1, 0, 1], index)

    def test_last_root_can_be_removed(self):
        self.assertEqual(core.parents_after_removal([-1], 0), [])

    def test_invalid_selection(self):
        for index in (-1, 3):
            with self.assertRaises(ValueError):
                core.parents_after_removal([-1, 0], index)


class WeightValidationTests(unittest.TestCase):
    def test_valid_weights(self):
        core.validate_weights([[('A', 1.0)], [('A', 0.2), ('B', 0.8)]], ['A', 'B'])

    def test_warning_without_exception_is_detected_from_weights(self):
        for vertices in [[], [[]], [[('Other', 1.0)]], [[('A', 0.0)]],
                         [[('A', 1.0)], []], [[('A', 1.0)]]]:
            with self.subTest(vertices=vertices), self.assertRaises(ValueError):
                core.validate_weights(vertices, ['A', 'B'])

    def test_bad_weight_values(self):
        for weight in (float('nan'), float('inf'), -0.5, 1.1):
            with self.subTest(weight=weight), self.assertRaises(ValueError):
                core.validate_weights([[('A', weight)]], ['A'])

    def test_no_deform_bones(self):
        with self.assertRaises(ValueError):
            core.validate_weights([[('A', 1.0)]], [])


class ExportPathTests(unittest.TestCase):
    def test_fbx_in_existing_asset_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Character.fbx"
            self.assertEqual(core.validate_export_path(path), str(path.resolve()))
            self.assertFalse(path.exists())

    def test_any_adjacent_chunk_manifest_blocks_destination(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / "CH_+000_+000.CHUNK.JSON").touch()
            for filename in ("geometry.fbx", "Character.fbx"):
                with self.subTest(filename=filename), self.assertRaisesRegex(ValueError, "manifest"):
                    core.validate_export_path(folder / filename)

    def test_missing_directory_and_wrong_extension(self):
        with tempfile.TemporaryDirectory() as directory:
            for path in (Path(directory) / "missing" / "Rig.fbx", Path(directory) / "Rig.json", ""):
                with self.subTest(path=path), self.assertRaises(ValueError):
                    core.validate_export_path(path)


class PoseContextTests(unittest.TestCase):
    def setUp(self):
        class Object:
            def __init__(self, name, mode, selected):
                self.name, self.mode, self.selected = name, mode, selected

            def select_get(self):
                return self.selected

            def select_set(self, selected):
                self.selected = selected

        class Objects(list):
            def get(self, name):
                return next((obj for obj in self if obj.name == name), None)

        class Context:
            scene = None

            @property
            def object(self):
                return self.view_layer.objects.active

        self.mesh = Object("mesh", "EDIT", True)
        self.rig = Object("rig", "OBJECT", False)
        objects = Objects([self.mesh, self.rig])
        objects.active = self.mesh
        self.context = Context()
        self.context.view_layer = SimpleNamespace(objects=objects)
        self.fail_pose = False

        def mode_set(mode):
            if self.fail_pose and mode == "POSE":
                return {"CANCELLED"}
            objects.active.mode = mode
            return {"FINISHED"}

        self.namespace = {
            "settings": lambda scene: None,
            "_rig": lambda context, value: self.rig,
            "contextmanager": contextmanager,
            "bpy": SimpleNamespace(ops=SimpleNamespace(object=SimpleNamespace(mode_set=mode_set))),
        }
        path = Path(__file__).resolve().parents[1] / "worldbuilder_chunks" / "easy_rigging.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        names = {"pose_rig", "preserve_context", "_live", "_object_mode", "_select_only"}
        functions = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in names]
        exec(compile(ast.Module(body=functions, type_ignores=[]), str(path), "exec"), self.namespace)

    def test_pose_failure_restores_selection_active_and_mode(self):
        self.fail_pose = True
        with self.assertRaisesRegex(ValueError, "Pose"):
            self.namespace["pose_rig"](self.context)
        self.assertIs(self.context.object, self.mesh)
        self.assertEqual(self.mesh.mode, "EDIT")
        self.assertTrue(self.mesh.selected)
        self.assertFalse(self.rig.selected)

    def test_pose_success_keeps_rig_selected_in_pose_mode(self):
        self.namespace["pose_rig"](self.context)
        self.assertIs(self.context.object, self.rig)
        self.assertEqual(self.rig.mode, "POSE")
        self.assertTrue(self.rig.selected)
        self.assertFalse(self.mesh.selected)

    def test_preserve_context_restores_success_and_failure(self):
        for failure in (False, True):
            with self.subTest(failure=failure):
                try:
                    with self.namespace["preserve_context"](self.context):
                        self.namespace["_select_only"](self.context, [self.rig], self.rig)
                        if failure:
                            raise ValueError("export failed")
                except ValueError:
                    pass
                self.assertIs(self.context.object, self.mesh)
                self.assertEqual(self.mesh.mode, "EDIT")
                self.assertTrue(self.mesh.selected)
                self.assertFalse(self.rig.selected)


if __name__ == "__main__":
    unittest.main()

import unittest

from _load_modules import contract


class ChunkCoordinateTests(unittest.TestCase):
    def test_positive_boundary_is_next_chunk(self):
        self.assertEqual(contract.chunk_coord_from_xy(0.0, 0.0, 0.0, 0.0, 128.0), (0, 0))
        self.assertEqual(contract.chunk_coord_from_xy(127.999, 0.0, 0.0, 0.0, 128.0), (0, 0))
        self.assertEqual(contract.chunk_coord_from_xy(128.0, 0.0, 0.0, 0.0, 128.0), (1, 0))

    def test_negative_values_use_floor(self):
        self.assertEqual(contract.chunk_coord_from_xy(-0.01, 0.0, 0.0, 0.0, 128.0), (-1, 0))
        self.assertEqual(contract.chunk_coord_from_xy(-128.0, -128.0, 0.0, 0.0, 128.0), (-1, -1))
        self.assertEqual(contract.chunk_coord_from_xy(-128.01, 0.0, 0.0, 0.0, 128.0), (-2, 0))

    def test_origin_offset(self):
        self.assertEqual(contract.chunk_coord_from_xy(10.0, 20.0, 10.0, 20.0, 8.0), (0, 0))
        self.assertEqual(contract.chunk_coord_from_xy(9.99, 19.99, 10.0, 20.0, 8.0), (-1, -1))

    def test_negative_region_floor_division(self):
        self.assertEqual(contract.region_coord(0, 0, 4), (0, 0))
        self.assertEqual(contract.region_coord(3, 3, 4), (0, 0))
        self.assertEqual(contract.region_coord(4, 4, 4), (1, 1))
        self.assertEqual(contract.region_coord(-1, -1, 4), (-1, -1))
        self.assertEqual(contract.region_coord(-4, -4, 4), (-1, -1))
        self.assertEqual(contract.region_coord(-5, -5, 4), (-2, -2))

    def test_touching_boundary_is_not_crossing(self):
        self.assertFalse(
            contract.bounds_cross_chunk_xy(0.0, 0.0, 128.0, 128.0, (0, 0), 0.0, 0.0, 128.0)
        )
        self.assertTrue(
            contract.bounds_cross_chunk_xy(0.0, 0.0, 128.02, 128.0, (0, 0), 0.0, 0.0, 128.0)
        )


class AuthoringLayerTests(unittest.TestCase):
    def test_floor_and_bounds_are_uniform(self):
        self.assertEqual(contract.layer_floor_z(0, 0.0, 16.0), 0.0)
        self.assertEqual(contract.layer_floor_z(3, 0.0, 16.0), 48.0)
        self.assertEqual(contract.layer_floor_z(2, -8.0, 16.0), 24.0)
        self.assertEqual(contract.layer_bounds_z(1, 0.0, 16.0), (16.0, 32.0))

    def test_index_uses_the_same_half_open_rule_as_chunks(self):
        self.assertEqual(contract.layer_index_for_z(0.0, 0.0, 16.0), 0)
        self.assertEqual(contract.layer_index_for_z(15.999, 0.0, 16.0), 0)
        self.assertEqual(contract.layer_index_for_z(16.0, 0.0, 16.0), 1)
        self.assertEqual(contract.layer_index_for_z(-0.01, 0.0, 16.0), -1)

    def test_clamp_keeps_the_index_inside_the_stack(self):
        self.assertEqual(contract.clamp_layer(-4, 8), 0)
        self.assertEqual(contract.clamp_layer(3, 8), 3)
        self.assertEqual(contract.clamp_layer(99, 8), 7)
        with self.assertRaises(ValueError):
            contract.clamp_layer(0, 0)

    def test_layer_names_round_trip(self):
        self.assertEqual(contract.layer_name(2), "LV_+0002")
        self.assertEqual(contract.parse_layer_name(contract.layer_name(-3)), -3)
        self.assertIsNone(contract.parse_layer_name("CH_+0001_+0001"))

    def test_zero_height_is_rejected(self):
        with self.assertRaises(ValueError):
            contract.layer_floor_z(1, 0.0, 0.0)


class EntityContractTests(unittest.TestCase):
    def test_flag_names_are_ordered_to_match_the_unity_enum(self):
        self.assertEqual(contract.entity_flag_names(False, False, False), [])
        self.assertEqual(
            contract.entity_flag_names(True, True, True),
            ["Persistent", "RegionStreamed", "Replicated"],
        )
        self.assertEqual(contract.entity_flag_names(False, True, False), ["RegionStreamed"])

    def test_kind_list_matches_the_unity_enum_order(self):
        self.assertEqual(contract.ENTITY_KINDS[0], "Generic")
        self.assertEqual(
            contract.ENTITY_KINDS,
            ("Generic", "Creature", "Resource", "DroppedItem", "Projectile", "Effect"),
        )


if __name__ == "__main__":
    unittest.main()

import math
import unittest

from _load_modules import biome_contract


class BiomeContractTests(unittest.TestCase):
    def test_normalize_name_and_attribute(self):
        self.assertEqual(biome_contract.normalize_name(" Deep Sea "), "DEEP_SEA")
        self.assertEqual(biome_contract.attribute_name("Kelp"), "WB_BIOME_KELP")

    def test_invalid_or_duplicate_ready_names(self):
        with self.assertRaises(ValueError):
            biome_contract.normalize_name("---")
        self.assertEqual(biome_contract.attribute_name("deep-sea"),
                         biome_contract.attribute_name("Deep Sea"))

    def test_clamp_and_non_finite_rejection(self):
        self.assertEqual(biome_contract.clamp_weight(-1.0), 0.0)
        self.assertEqual(biome_contract.clamp_weight(2.0), 1.0)
        with self.assertRaises(ValueError):
            biome_contract.clamp_weight(math.nan)

    def test_normalize_weights_preserves_zero_and_limits_sum(self):
        self.assertEqual(biome_contract.normalize_weights([0.0, 0.0]), [0.0, 0.0])
        values = biome_contract.normalize_weights([0.8, 0.8, 0.4])
        self.assertAlmostEqual(sum(values), 1.0)
        self.assertEqual(values, [0.4, 0.4, 0.2])

    def test_barycentric_interpolation_weights(self):
        values = biome_contract.barycentric_weights(
            (0.25, 0.25, 0.0), (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0))
        self.assertAlmostEqual(sum(values), 1.0)
        self.assertEqual(tuple(round(value, 2) for value in values), (0.5, 0.25, 0.25))

    def test_manifest_is_deterministic_and_skips_disabled_exports(self):
        document = biome_contract.build_manifest("Terrain", [
            {"stable_id": "b", "name": "Rock", "attribute_name": "WB_BIOME_ROCK", "export_enabled": True},
            {"stable_id": "a", "name": "Kelp", "attribute_name": "WB_BIOME_KELP", "export_enabled": True},
            {"stable_id": "c", "name": "Hidden", "attribute_name": "WB_BIOME_HIDDEN", "export_enabled": False},
        ])
        self.assertEqual(document["schemaVersion"], 1)
        self.assertEqual([item["name"] for item in document["layers"]], ["Kelp", "Rock"])

    def test_schema_migration_accepts_v1_and_rejects_unknown(self):
        self.assertEqual(biome_contract.migrate_document({"layers": []})["schemaVersion"], 1)
        with self.assertRaises(ValueError):
            biome_contract.migrate_document({"schemaVersion": 2})


if __name__ == "__main__":
    unittest.main()

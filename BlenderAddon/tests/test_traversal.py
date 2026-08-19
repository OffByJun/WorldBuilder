import unittest

from _load_modules import analysis, contract


class DepthBandTests(unittest.TestCase):
    def test_surface_and_above_are_band_zero(self):
        self.assertEqual(contract.depth_band_index(0.0, 0.0, 20, 60, 120), 0)
        self.assertEqual(contract.depth_band_index(0.0, 5.0, 20, 60, 120), 0)
        self.assertEqual(contract.depth_below(0.0, 5.0), 0.0)

    def test_bands_use_cumulative_thickness(self):
        self.assertEqual(contract.depth_band_index(0.0, -20.0, 20, 60, 120), 1)
        self.assertEqual(contract.depth_band_index(0.0, -20.01, 20, 60, 120), 2)
        self.assertEqual(contract.depth_band_index(0.0, -80.0, 20, 60, 120), 2)
        self.assertEqual(contract.depth_band_index(0.0, -80.01, 20, 60, 120), 3)
        self.assertEqual(contract.depth_band_index(0.0, -200.0, 20, 60, 120), 3)
        self.assertEqual(contract.depth_band_index(0.0, -200.01, 20, 60, 120), 4)

    def test_sea_level_offsets_every_band(self):
        self.assertEqual(contract.depth_band_index(100.0, 90.0, 20, 60, 120), 1)
        self.assertEqual(contract.depth_band_boundaries(100.0, 20, 60, 120), [100.0, 80.0, 20.0, -100.0])

    def test_names_are_clamped(self):
        self.assertEqual(contract.depth_band_name(0), "Surface")
        self.assertEqual(contract.depth_band_name(4), "Abyss")
        self.assertEqual(contract.depth_band_name(99), "Abyss")

    def test_negative_thickness_is_rejected(self):
        with self.assertRaises(ValueError):
            contract.depth_band_index(0.0, -1.0, -1, 60, 120)


class WalkProbeTests(unittest.TestCase):
    def test_missing_ground_reports_first(self):
        self.assertEqual(analysis.walk_status(False, 0, 10, 45, 1.8), analysis.NO_GROUND)

    def test_slope_beats_headroom_when_both_fail(self):
        self.assertEqual(analysis.walk_status(True, 70, 0.5, 45, 1.8), analysis.STEEP)

    def test_low_ceiling(self):
        self.assertEqual(analysis.walk_status(True, 10, 1.2, 45, 1.8), analysis.LOW_CEILING)
        self.assertEqual(analysis.walk_status(True, 10, 1.8, 45, 1.8), analysis.OK)

    def test_boundary_slope_passes(self):
        self.assertEqual(analysis.walk_status(True, 45, 5, 45, 1.8), analysis.OK)
        self.assertEqual(analysis.walk_status(True, 45.01, 5, 45, 1.8), analysis.STEEP)

    def test_slope_from_normal(self):
        self.assertAlmostEqual(analysis.slope_from_normal_z(1.0), 0.0, places=5)
        self.assertAlmostEqual(analysis.slope_from_normal_z(0.0), 90.0, places=5)
        self.assertAlmostEqual(analysis.slope_from_normal_z(-1.0), 0.0, places=5)


class SwimProbeTests(unittest.TestCase):
    def test_narrow_and_blocked(self):
        self.assertEqual(analysis.swim_status(0.0, 0.4), analysis.BLOCKED)
        self.assertEqual(analysis.swim_status(0.2, 0.4), analysis.NARROW)
        self.assertEqual(analysis.swim_status(0.4, 0.4), analysis.OK)


class SummaryTests(unittest.TestCase):
    def test_counts_and_ratio(self):
        counts = analysis.summarize([analysis.OK, analysis.OK, analysis.STEEP, analysis.NO_GROUND])
        self.assertEqual(counts["total"], 4)
        self.assertEqual(counts[analysis.OK], 2)
        self.assertEqual(counts[analysis.STEEP], 1)
        self.assertAlmostEqual(counts["pass_ratio"], 0.5)

    def test_empty_summary_does_not_divide_by_zero(self):
        counts = analysis.summarize([])
        self.assertEqual(counts["total"], 0)
        self.assertEqual(counts["pass_ratio"], 0.0)


class LayerCellTests(unittest.TestCase):
    def test_layer_separates_stacked_cells(self):
        low = analysis.cell_coord_3d((5.0, 5.0, 0.0), (0, 0), 32, 0)
        high = analysis.cell_coord_3d((5.0, 5.0, 90.0), (0, 0), 32, 3)
        self.assertEqual(low, (0, 0, 0))
        self.assertEqual(high, (0, 0, 3))
        self.assertNotEqual(low, high)


if __name__ == "__main__":
    unittest.main()

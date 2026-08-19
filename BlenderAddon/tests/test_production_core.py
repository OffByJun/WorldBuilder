import json
import math
import unittest

from worldbuilder_chunks import analysis, biome_brush, chunk_clipper, chunk_terrain_math, exclusion, localization, manifest, scatter_rules, scatter_state, seam, spline_mesh, spline_sampling, stamp_io, terrain_carve, vertex_bake_contract


class BrushTests(unittest.TestCase):
    def test_falloffs_and_blending(self):
        self.assertAlmostEqual(biome_brush.falloff("LINEAR", 0.25), 0.75)
        self.assertEqual(biome_brush.falloff("CONSTANT", 1.0), 0.0)
        self.assertAlmostEqual(biome_brush.erase(1.0, 0.5), 0.5)
        self.assertAlmostEqual(biome_brush.smooth(0.0, [1.0, 1.0], 0.5), 0.5)

    def test_auto_normalize(self):
        value = biome_brush.auto_normalize([0.2, 0.8, 0.4], 0, 0.6)
        self.assertAlmostEqual(sum(value), 1.0)
        self.assertAlmostEqual(value[0], 0.6)


class ScatterTests(unittest.TestCase):
    def test_deterministic_weighted_and_id(self):
        seed = scatter_rules.stable_seed("layer", 4)
        self.assertEqual(scatter_rules.weighted_index([1, 4, 0], seed), scatter_rules.weighted_index([1, 4, 0], seed))
        self.assertIsNone(scatter_rules.weighted_index([0, 0], seed))
        self.assertEqual(scatter_state.instance_id("a", "b"), scatter_state.instance_id("a", "b"))

    def test_spatial_hash(self):
        value = scatter_rules.SpatialHash(2.0)
        value.insert((0, 0, 0))
        self.assertFalse(value.can_insert((1, 0, 0)))
        self.assertTrue(value.can_insert((2, 0, 0)))

    def test_rules_and_merge(self):
        ok, reason = scatter_rules.evaluate_candidate({"position": (0,0,5), "normal": (0,0,1), "biome_weight": .8}, {"min_height": 0, "max_height": 10, "max_slope": 20, "biome_min": .5})
        self.assertTrue(ok, reason)
        old = [{"instance_id":"1", "state":scatter_state.MANUALLY_MOVED, "position":(2,0,0)}]
        new = [{"instance_id":"1", "state":scatter_state.GENERATED, "position":(0,0,0)}]
        self.assertEqual(scatter_state.merge_rebuild(old,new)[0]["position"], (2,0,0))


class GeometryTests(unittest.TestCase):
    def test_authoritative_chunk_terrain_has_identical_shared_edge(self):
        settings={"seed":3,"feature_size":10,"relief":5,"base_height":-2,"preset":"RIDGED"}
        left=chunk_terrain_math.chunk_vertices((0,0),(0,0),16,8,settings);right=chunk_terrain_math.chunk_vertices((1,0),(0,0),16,8,settings)
        for row in range(9):self.assertAlmostEqual(left[row*9+8][2],right[row*9][2],places=9)
        self.assertEqual(chunk_terrain_math.chunk_faces((-1,0),8),chunk_terrain_math.chunk_faces((-1,0),8))
    def test_chunk_triangle_clipping_preserves_area_uv_and_negative_coordinates(self):
        triangle={"vertices":[{"position":(-1,0,0),"uv":(0,0),"normal":(0,0,1)},{"position":(1,0,0),"uv":(1,0),"normal":(0,0,1)},{"position":(0,2,0),"uv":(.5,1),"normal":(0,0,1)}],"material":2}
        outputs=chunk_clipper.clip_triangles([triangle],chunk_size=1)
        self.assertIn((-1,0),outputs);self.assertIn((0,0),outputs)
        area=0.0
        for value in outputs.values():
            for face in value["faces"]:
                points=[value["vertices"][index]["position"] for index in face]
                area+=abs((points[1][0]-points[0][0])*(points[2][1]-points[0][1])-(points[1][1]-points[0][1])*(points[2][0]-points[0][0]))*.5
            self.assertTrue(all(material==2 for material in value["materials"]))
            self.assertTrue(all("uv" in vertex for vertex in value["vertices"]))
        self.assertAlmostEqual(area,2.0,places=6)
    def test_exclusion(self):
        self.assertTrue(exclusion.point_inside_box((0,0,0),(0,0,0),(1,1,1)))
        self.assertTrue(exclusion.point_inside_sphere((1,0,0),(0,0,0),1))
        self.assertAlmostEqual(exclusion.distance_to_curve_xy((1,1,0),[(0,0,0),(2,0,0)]),1)

    def test_spline_sampling_and_carve(self):
        samples=spline_sampling.sample_polyline([(0,0,0),(10,0,0)],2)
        self.assertEqual(len(samples),6)
        carved=terrain_carve.carve_vertex((5,0,2),[(0,0,0),(10,0,0)],2,1)
        self.assertLess(carved[2],2)
        swept=spline_mesh.sweep(samples,1,8)
        self.assertEqual(len(swept["vertices"]),48)
        self.assertEqual(len(swept["faces"]),40)

    def test_terrain_rebuild_keeps_vertices_outside_spline_influence(self):
        basis=[(5,0,2),(500,500,7)]
        result=terrain_carve.rebuild_vertices(basis,[(0,0,0),(10,0,0)],{"width":2,"depth":1,"falloff":1})
        self.assertLess(result[0][2],2)
        self.assertEqual(result[1],basis[1])

    def test_seam_negative_neighbor_and_stitch(self):
        self.assertEqual(seam.neighbor((-1,-1),"WEST"),(-2,-1))
        a=[(0,0,0),(1,0,1)]; b=[(0,0,2),(1,0,3)]
        pairs,status=seam.match_edges(a,b,"NORTH")
        self.assertEqual(status,"OK")
        left,right=seam.stitched_positions(a,b,pairs)
        self.assertEqual(left,right)


class ContractTests(unittest.TestCase):
    def test_korean_localization_and_collection_registry_contract(self):
        self.assertEqual(localization.MESSAGES["ko"]["terrain_fill"],"선택 청크 지형 생성")
        value=stamp_io.create_asset_registry([{"assetId":"arch.01","collectionName":"StoneArch"}])
        self.assertEqual(stamp_io.validate_asset_registry(value),[])
    def test_manifest_stable_sort(self):
        a=manifest.build_chunk_manifest("w",(-1,2),"p",[{"stableId":"b"},{"stableId":"a"}])
        b=manifest.build_chunk_manifest("w",(-1,2),"p",list(reversed([{"stableId":"b"},{"stableId":"a"}])))
        self.assertEqual(manifest.canonical_json(a),manifest.canonical_json(b))

    def test_stamp_and_analysis(self):
        value=stamp_io.create_stamp("x","id","rocks",(0,0,0),{},[{"name":"b","assetId":"2"},{"name":"a","assetId":"1"}])
        self.assertFalse(stamp_io.validate_stamp(value))
        cells=analysis.aggregate_objects([{"position":(-1,-1,0),"triangles":3}],cell_size=32)
        self.assertIn((-1,-1),cells)

    def test_stamp_asset_registry_is_deterministic_and_rejects_duplicates(self):
        entries=[{"assetId":"b","objectName":"RockB","blendPath":"rocks.blend"},{"assetId":"a","objectName":"RockA","blendPath":"rocks.blend"}]
        value=stamp_io.create_asset_registry(entries)
        self.assertEqual([item["assetId"] for item in value["assets"]],["a","b"])
        duplicate=stamp_io.create_asset_registry([entries[0],entries[0]])
        self.assertIn("assetId values must be unique",stamp_io.validate_asset_registry(duplicate))

    def test_vertex_contract(self):
        color=vertex_bake_contract.bake_rgba({"height":5,"normal":(0,0,1),"biome_weight":.4},("HEIGHT_NORMALIZED","UP_FACING","BIOME_WEIGHT","CONSTANT"),{"R":{"min_height":0,"max_height":10},"A":{"constant":.25}})
        self.assertEqual(color,(.5,1.0,.4,.25))


if __name__ == "__main__":
    unittest.main()

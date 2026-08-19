import unittest

from _load_modules import profile


VALID = {
    "version": 1,
    "worldId": "SurvivalWorld",
    "chunkSize": 128.0,
    "chunksPerRegion": 4,
    "queryCellSize": 32.0,
    "worldOrigin": {"x": 0.0, "z": 0.0},
    "coordinateSystem": {
        "blenderPlane": "XY",
        "unityPlane": "XZ",
        "vectorMapping": "XZY",
    },
}


class ProfileTests(unittest.TestCase):
    def test_valid_profile(self):
        result = profile.validate_document(VALID)
        self.assertEqual(result["worldId"], "SurvivalWorld")
        self.assertEqual(result["chunkSize"], 128.0)

    def test_invalid_mapping(self):
        document = {**VALID, "coordinateSystem": {**VALID["coordinateSystem"], "vectorMapping": "XYZ"}}
        with self.assertRaises(ValueError):
            profile.validate_document(document)

    def test_non_positive_size(self):
        document = {**VALID, "chunkSize": 0}
        with self.assertRaises(ValueError):
            profile.validate_document(document)


if __name__ == "__main__":
    unittest.main()

from pathlib import Path
import importlib.util
import sys
import types

ROOT = Path(__file__).resolve().parents[1] / "worldbuilder_chunks"

package = sys.modules.get("worldbuilder_chunks")
if package is None:
    package = types.ModuleType("worldbuilder_chunks")
    package.__path__ = [str(ROOT)]
    sys.modules["worldbuilder_chunks"] = package


def load(name):
    full_name = f"worldbuilder_chunks.{name}"
    if full_name in sys.modules:
        return sys.modules[full_name]
    spec = importlib.util.spec_from_file_location(full_name, ROOT / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[full_name] = module
    setattr(package, name, module)
    spec.loader.exec_module(module)
    return module


contract = load("contract")
profile = load("profile")
biome_contract = load("biome_contract")
analysis = load("analysis")

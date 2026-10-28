"""Compatibility entry point. v2 packages full locks along curved contours with Swift.
The old polygon/inpaint pipeline is archived in Rollback/HairLayers-v1.
"""
import os
from pathlib import Path
import subprocess
root = Path(__file__).resolve().parents[1]
env = dict(os.environ, CLANG_MODULE_CACHE_PATH='/tmp/traigger-clang', SWIFT_MODULECACHE_PATH='/tmp/traigger-swift')
subprocess.run(['swift', str(root / 'Tools/SplitFrontHairLayers.swift')], cwd=root, env=env, check=True)

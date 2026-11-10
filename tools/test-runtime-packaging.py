#!/usr/bin/env python3
import importlib.util
import pathlib
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('runtime_sources', pathlib.Path(__file__).with_name('package-runtime-sources.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RuntimeSourcesTests(unittest.TestCase):
    def test_vr_selects_commonlib_and_includes_embedded_openvr_source_and_license(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            sources = root / 'src'
            for file in ['common/include/RE/Skyrim.h', 'common/CMakeLists.txt', 'common/COPYING',
                         'openvr/CMakeLists.txt', 'common/extern/openvr/headers/openvr.h', 'common/extern/openvr/LICENSE']:
                target = sources / file
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(file)
            module.package_sources(sources, root / 'out')
            with tarfile.open(root / 'out/source/CommonLibSSE-NG.tar.gz') as archive:
                self.assertIn('./include/RE/Skyrim.h', archive.getnames())
                self.assertIn('./extern/openvr/headers/openvr.h', archive.getnames())
                self.assertIn('./extern/openvr/LICENSE', archive.getnames())
            self.assertEqual('common/extern/openvr/LICENSE', (root / 'out/licenses/OpenVR.txt').read_text())

    def test_ambiguous_commonlib_trees_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            for name in ['first', 'second']:
                header = root / name / 'include/RE/Skyrim.h'
                header.parent.mkdir(parents=True)
                header.write_text('fixture')
            with self.assertRaises(ValueError):
                module.package_sources(root, root / 'out')
            self.assertFalse((root / 'out').exists())


if __name__ == '__main__':
    unittest.main()

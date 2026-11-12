#!/usr/bin/env python3
import importlib.util
import pathlib
import re
import shutil
import tempfile
import unittest
import zipfile

tools = pathlib.Path(__file__).resolve().parent


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, tools / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


licenses = load('runtime_licenses', 'package-runtime-licenses.py')
runtime = load('runtime_package', 'package-runtime.py')


class RuntimeLicenseTests(unittest.TestCase):
    def test_vr_selects_commonlib_and_copies_only_the_openvr_license(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            sources = root / 'src'
            for file in ['common/include/RE/Skyrim.h', 'common/CMakeLists.txt', 'common/COPYING',
                         'openvr/CMakeLists.txt', 'common/extern/openvr/headers/openvr.h', 'common/extern/openvr/LICENSE']:
                target = sources / file
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(file)
            licenses.collect_licenses(sources, root / 'out')
            self.assertEqual(['licenses/OpenVR.txt'],
                             [p.relative_to(root / 'out').as_posix() for p in (root / 'out').rglob('*') if p.is_file()])
            self.assertEqual('common/extern/openvr/LICENSE', (root / 'out/licenses/OpenVR.txt').read_text())

    def test_commonlib_without_openvr_adds_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            header = root / 'src/common/include/RE/Skyrim.h'
            header.parent.mkdir(parents=True)
            header.write_text('fixture')
            licenses.collect_licenses(root / 'src', root / 'out')
            self.assertFalse((root / 'out').exists())

    def test_openvr_without_a_license_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            for file in ['common/include/RE/Skyrim.h', 'common/extern/openvr/headers/openvr.h']:
                target = root / 'src' / file
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(file)
            with self.assertRaises(ValueError):
                licenses.collect_licenses(root / 'src', root / 'out')

    def test_ambiguous_commonlib_trees_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            for name in ['first', 'second']:
                header = root / name / 'include/RE/Skyrim.h'
                header.parent.mkdir(parents=True)
                header.write_text('fixture')
            with self.assertRaises(ValueError):
                licenses.collect_licenses(root, root / 'out')
            self.assertFalse((root / 'out').exists())


class RuntimePackageTests(unittest.TestCase):
    def fixture(self, root):
        repo = tools.parent
        for file in ['VERSION', 'LICENSE.txt', 'docs/UPGRADING.md', 'packaging/PenguLOD.ini', 'assets/pengulod-256.png']:
            (root / file).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(repo / file, root / file)
        shutil.copytree(repo / 'packaging/fomod', root / 'packaging/fomod')
        for line in runtime.LINES:
            build = root / 'dist/runtime' / line
            (build / 'SKSE/Plugins').mkdir(parents=True)
            (build / 'SKSE/Plugins/PenguLOD.dll').write_bytes(b'MZ' + line.encode())
            (build / 'licenses').mkdir()
            (build / 'licenses/commonlibsse-ng.txt').write_text('notice')
            # Left by a build made before sources were dropped from the package.
            (build / 'source').mkdir()
            (build / 'source/CommonLibSSE-NG.tar.gz').write_bytes(b'stale')

    def test_package_holds_the_installer_dlls_and_notices_but_no_source(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self.fixture(root)
            output = runtime.package(root)
            version = (root / 'VERSION').read_text().strip()
            self.assertEqual(f'PenguLOD-{version}-SKSE.zip', output.name)
            with zipfile.ZipFile(output) as archive:
                names = set(archive.namelist())
                info = archive.read('fomod/info.xml').decode()
                config = archive.read('fomod/ModuleConfig.xml').decode()
            expected = {'LICENSE.txt', 'README.md', 'common/SKSE/Plugins/PenguLOD.ini',
                        'fomod/info.xml', 'fomod/ModuleConfig.xml', 'fomod/images/pengulod.png'}
            for line in runtime.LINES:
                expected |= {f'{line}/SKSE/Plugins/PenguLOD.dll', f'{line}/licenses/commonlibsse-ng.txt'}
            self.assertEqual(expected, names)
            self.assertIn(f'<Version>{version}</Version>', info)
            # Every file or folder the installer copies exists in the package.
            for source in re.findall(r'source="([^"]+)"', config):
                path = source.replace('\\', '/')
                self.assertTrue(any(n == path or n.startswith(path + '/') for n in names), source)
            self.assertIn(output.name, (root / 'dist/SHA256SUMS-RUNTIME').read_text())

    def test_a_missing_dll_stops_packaging(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self.fixture(root)
            (root / 'dist/runtime/1.7.x/SKSE/Plugins/PenguLOD.dll').write_bytes(b'not a dll')
            with self.assertRaises(SystemExit):
                runtime.package(root)


if __name__ == '__main__':
    unittest.main()

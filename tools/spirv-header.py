"""Embed compiled SPIR-V as aligned uint32 words for the native compute helper."""
import pathlib
import struct
import sys

source = pathlib.Path(sys.argv[1]).read_bytes()
words = struct.unpack(f'<{len(source) // 4}I', source)
pathlib.Path(sys.argv[2]).write_text(
    '#include <stdint.h>\nstatic const uint32_t bc7_spv[] = {' +
    ','.join(hex(word) for word in words) + '};\n', encoding='utf-8')

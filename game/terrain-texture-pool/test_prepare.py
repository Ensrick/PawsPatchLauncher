"""Preparation safety checks using tiny TGCK fixtures, no game launch."""
import struct
import tempfile
import unittest
from pathlib import Path
import prepare


def archive(data, names=None):
    names = names or [prepare.SOURCE]
    footer = struct.pack('<I', len(names))
    for name in names:
        footer += struct.pack('<H', len(name)) + name.encode('utf-16-le')
        footer += struct.pack('<qqIi', 0, len(data), 0, 0)
    sections = [('Files', 12, len(data)), ('Footer', 12 + len(data), len(footer)),
                ('Header', 12, 0)]
    table = b''.join(n.encode('utf-16-le').ljust(64, b'\0') + struct.pack('<qqQq', o, s, 0, s)
                     for n, o, s in sections)
    return struct.pack('<4sII', b'TGCK', 2, 3) + data + footer + table


class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / 'game'
        self.game.mkdir()
        self.stock = (Path(__file__).parent / 'data' / prepare.TARGET).read_bytes().replace(prepare.LINE, b'')
        self.rwd = self.game / 'Data.rwd'
        self.rwd.write_bytes(archive(self.stock))
        self.out = self.root / 'output'

    def test_mod_uvars_preserved(self):
        (self.game / 'data').mkdir()
        path = self.game / 'data/UVars.tgi'
        custom = b'[Vars]\r\n{\r\n int TerrainTexturePoolDesiredNumEntries = 360\r\n int TerrainQuadSize = 1\r\n}\r\n'
        path.write_bytes(custom)
        before = self.rwd.read_bytes()
        prepare.prepare_game(self.game, self.out)
        self.assertEqual(path.read_bytes(), custom)
        self.assertEqual(self.rwd.read_bytes(), before)
        self.assertEqual((self.out / prepare.TARGET).read_bytes().replace(prepare.LINE, b''), self.stock)

    def test_custom_override_rejected_without_output(self):
        path = self.game / prepare.TARGET
        path.parent.mkdir()
        path.write_bytes(self.stock + b';; custom')
        with self.assertRaisesRegex(ValueError, 'Custom'): prepare.prepare_game(self.game, self.out)
        self.assertFalse(self.out.exists())
        self.assertEqual(path.read_bytes(), self.stock + b';; custom')

    def test_existing_output_preserved(self):
        self.out.mkdir()
        (self.out / 'keep').write_bytes(b'keep')
        with self.assertRaises(ValueError): prepare.prepare_game(self.game, self.out)
        self.assertEqual((self.out / 'keep').read_bytes(), b'keep')

    def test_output_in_game_rejected(self):
        with self.assertRaisesRegex(ValueError, 'separate'):
            prepare.prepare_game(self.game, self.game / 'experimental')
        self.assertFalse((self.game / 'experimental').exists())

    def test_unsupported_base_rejected(self):
        self.rwd.write_bytes(archive(self.stock + b'changed'))
        with self.assertRaises(ValueError): prepare.prepare_game(self.game, self.out)
        self.assertFalse(self.out.exists())

    def test_duplicate_entry_rejected(self):
        self.rwd.write_bytes(archive(self.stock, [prepare.SOURCE, prepare.SOURCE.upper()]))
        with self.assertRaisesRegex(ValueError, 'twice'): prepare.read_entry(self.rwd, prepare.SOURCE)

    def test_oversized_config_rejected(self):
        self.rwd.write_bytes(archive(b' ' * (1024 * 1024 + 1)))
        with self.assertRaisesRegex(ValueError, '1 MiB'): prepare.read_entry(self.rwd, prepare.SOURCE)

    def test_section_outside_file_rejected(self):
        raw = bytearray(archive(self.stock))
        struct.pack_into('<q', raw, len(raw) - 288 + 64, len(raw) + 100)
        self.rwd.write_bytes(raw)
        with self.assertRaisesRegex(ValueError, 'outside'): prepare.read_entry(self.rwd, prepare.SOURCE)


if __name__ == '__main__':
    unittest.main()

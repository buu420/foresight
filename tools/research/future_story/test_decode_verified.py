"""PC instruction boundaries checked against the supported executable handlers."""
import unittest
from decode_verified import instruction, walk


class PcInstructionTests(unittest.TestCase):
    def test_variable_item_grant_uses_one_local_word_operand(self):
        # Cyrus's room: C7 local0E then CB item003D, FC01.
        code = bytes.fromhex('c70ecb3d00fc0100')
        self.assertEqual((0xc7, bytes.fromhex('0e')), instruction(code, 0))
        self.assertEqual([0, 2, 5, 7], sorted(walk(code, [0])))

    def test_pc_only_handlers_do_not_decode_operands_as_opcodes(self):
        # 1695B0,1697D0,16D100,1698A0, inline BE,169960.
        for op, count in [(0x79, 6), (0x85, 3), (0x86, 3), (0x93, 1), (0x9b, 2), (0xbe, 7), (0xbf, 1)]:
            with self.subTest(op=hex(op)):
                code = bytes([op]) + bytes([1]) * count + bytes([0])
                self.assertEqual((op, bytes([1]) * count), instruction(code, 0))
                self.assertEqual([0, count + 1], sorted(walk(code, [0])))

    def test_truncated_pc_operands_remain_unknown(self):
        self.assertEqual((0xbe, None), instruction(bytes.fromhex('be0102'), 0))


if __name__ == '__main__':
    unittest.main()

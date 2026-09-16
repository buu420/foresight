import os
from pathlib import Path
import subprocess
import sys
import unittest
from script_navigation import paths, compare, intervals, coordinate_locals, region_actions, guard, simplify


class ScriptNavigationTests(unittest.TestCase):
    def test_simplification_is_reproducible_across_hash_seeds(self):
        probe = '''import json
from script_navigation import guard, simplify
rows = [dict(Kind='Warp', Destination=15,
             Guards=[guard('Global', bit, 0, 1, bool(n & (1 << bit))) for bit in range(3)])
        for n in (0, 1, 2, 5, 6, 7)]
print(json.dumps(simplify(rows), sort_keys=True))'''
        outputs = set()
        for seed in range(4):
            env = dict(os.environ, PYTHONHASHSEED=str(seed),
                       PYTHONPATH=str(Path(__file__).resolve().parent))
            outputs.add(subprocess.check_output([sys.executable, '-c', probe], env=env, text=True))
        self.assertEqual(1, len(outputs))

    def test_simplification_preserves_every_three_flag_truth_table(self):
        for truth_table in range(256):
            enabled = {n for n in range(8) if truth_table & (1 << n)}
            rows = [dict(Kind='Warp', Destination=15,
                         Guards=[guard('Global', bit, 0, 1, bool(n & (1 << bit))) for bit in range(3)])
                    for n in sorted(enabled)]
            reduced = simplify(rows)
            actual = {n for n in range(8)
                      if any(all(bool(n & (1 << g['Index'])) == g['Expected'] for g in row['Guards'])
                             for row in reduced)}
            self.assertEqual(enabled, actual, f'truth table {truth_table}')

    def test_flag_only_floor_switch_is_an_interaction(self):
        actions, complete = paths(bytes.fromhex('16840186020065808400'), [], 0)
        self.assertTrue(complete)
        switch = next(a for a in actions if a['Kind'] == 'Switch')
        self.assertEqual('Global', switch['Source'])
        self.assertEqual(0x184, switch['Index'])
        self.assertEqual(1, switch['Value'])

    def test_extended_bit_write_cannot_test_the_previous_frame_value(self):
        actions, complete = paths(bytes.fromhex('4503106e10080605ca010000'), [], 0)
        self.assertTrue(complete)
        item = next(a for a in actions if a['Kind'] == 'Item')
        self.assertEqual([], item['Guards'])

    def test_collected_flag_branch_does_not_offer_item_again(self):
        # If upper-global140 bit0 is set, return; otherwise grant an item.
        code = bytes.fromhex('164001860200ca010000')
        actions, complete = paths(code, [], 0)
        self.assertTrue(complete)
        item = next(a for a in actions if a['Kind'] == 'Item')
        self.assertEqual([dict(Source='Global', Index=0x140, Operation=6, Value=1, Expected=False)], item['Guards'])

    def test_native_or_comparison_tests_nonzero(self):
        self.assertTrue(compare(0, 1, 7))
        self.assertFalse(compare(0, 0, 7))

    def test_item_money_and_party_prerequisites_are_preserved(self):
        for code, source, index, operation, value in [
                ('c9011005ca020000', 'Item', 0x1001, 2, 0),
                ('ccf40105ca020000', 'Gold', 0, 4, 500),
                ('d20505ca020000', 'ActiveParty', 5, 0, 1),
                ('cf0405ca020000', 'Recruited', 4, 0, 1)]:
            actions, complete = paths(bytes.fromhex(code), [], 0)
            self.assertTrue(complete)
            item = next(a for a in actions if a['Kind'] == 'Item')
            self.assertEqual([dict(Source=source, Index=index, Operation=operation, Value=value, Expected=True)], item['Guards'])

    def test_disconnected_coordinate_predicates_stay_disconnected(self):
        self.assertEqual([[1, 2], [4, 4]], intervals({1, 2, 4}))

    def test_missing_instruction_or_budget_does_not_publish_partial_paths(self):
        self.assertEqual(([], False), paths(bytes.fromhex('ca0100be0102'), [], 0))
        self.assertEqual(([], False), paths(bytes.fromhex('ca010000'), [], 0, budget=1))

    def test_condition_after_local_write_uses_written_value(self):
        code = bytes.fromhex('7506120601000400ca010000')
        actions, complete = paths(code, [], 0)
        self.assertTrue(complete)
        self.assertFalse(any(a['Kind'] == 'Item' for a in actions))

    def test_mutually_exclusive_native_flag_values_do_not_become_an_action(self):
        # The same unchanged byte cannot equal both 1 and 2.
        code = bytes.fromhex('161001000d1610020008ca01000000')
        actions, complete = paths(code, [], 0)
        self.assertTrue(complete)
        self.assertFalse(any(a['Kind'] == 'Item' for a in actions))

    def test_spatial_region_requires_player_coordinate_provenance_on_that_path(self):
        suffix = '1204080008dd240001010100'
        actions, complete = paths(bytes.fromhex('22000406' + suffix), [], 0, {4: 'X', 6: 'Y'})
        self.assertTrue(complete)
        warp = next(a for a in actions if a['Kind'] == 'Warp')
        self.assertEqual('X', warp['Guards'][0]['Source'])
        # A subsequent read of a different actor invalidates those coordinates.
        actions, complete = paths(bytes.fromhex('2200040621020406' + suffix), [], 0, {4: 'X', 6: 'Y'})
        self.assertTrue(complete)
        self.assertEqual([], next(a for a in actions if a['Kind'] == 'Warp')['Guards'])
        # Touch handlers execute the read during interaction, not in the old frame.
        actions, complete = paths(bytes.fromhex('22000406' + suffix), [], 0)
        self.assertTrue(complete)
        self.assertEqual([], next(a for a in actions if a['Kind'] == 'Warp')['Guards'])

    def test_a_room_controller_can_supply_a_pillars_coordinate_cells(self):
        code = bytes.fromhex('2200040600b2001204080008dd240001010100')
        actors = [[0] + [5] * 15, [6] * 16]
        self.assertEqual({4: 'X', 6: 'Y'}, coordinate_locals(code, actors))
        region = next(r for r in region_actions(code, actors) if r['Actor'] == 1)
        self.assertEqual((8, 8, 36), (region['Left'], region['Right'], region['Destination']))
        # Reusing that cell for a non-coordinate value destroys the global alias.
        extra = len(code)
        changed = code + bytes.fromhex('4f010400')
        actors.append([extra] * 16)
        self.assertNotIn(4, coordinate_locals(changed, actors))
        self.assertFalse(any(r['Actor'] == 1 for r in region_actions(changed, actors)))

    def test_party_callback_can_finish_a_shared_local_wait(self):
        # Clear local07; schedule party function; wait for local07 >= 3;
        # warp to End of Time. The callback's update must not be frozen at zero.
        code = bytes.fromhex('770705001412070303031106ddd001010d0a00')
        actions, complete = paths(code, [], 0)
        self.assertTrue(complete)
        warp = next(a for a in actions if a['Kind'] == 'Warp')
        self.assertEqual(464, warp['Destination'])
        self.assertEqual([], warp['Guards'])

    def test_spatial_encounter_stops_before_post_battle_cinematic(self):
        # Only the current walk into battle is navigable. Post-battle story
        # progress occurs under game control, after the field frame expires.
        code = bytes.fromhex('00220004061204080008d890c05a5100')
        regions = region_actions(code, [[0] * 16])
        encounter = next(r for r in regions if r['Kind'] == 'Encounter')
        self.assertEqual((8, 8), (encounter['Left'], encounter['Right']))
        self.assertFalse(any(r['Kind'] == 'Progress' for r in regions))

    def test_local_only_terrain_switch_has_a_bounded_floor_target(self):
        # Giant's Claw uses tile copies without a persistent quest write.
        code = bytes.fromhex('0022000406120408000de5000001010a1a0100')
        regions = region_actions(code, [[0] * 16])
        switch = next(r for r in regions if r['Kind'] == 'Terrain')
        self.assertEqual((8, 8), (switch['Left'], switch['Right']))
        actions, complete = paths(bytes.fromhex('e5000001010a1a0100'), [], 0)
        self.assertTrue(complete)
        self.assertEqual(['Terrain'], [a['Kind'] for a in actions])

    def test_terrain_copy_preserves_native_bounds_planes_and_guard(self):
        actions, complete = paths(bytes.fromhex('120600000de856e5030005021e263b750600'), [], 0)
        self.assertTrue(complete)
        terrain = next(a for a in actions if a['Kind'] == 'Terrain')
        self.assertEqual(dict(Left=3, Top=0, Right=5, Bottom=2, X=30, Y=38, Flags=59), terrain['Copy'])
        self.assertEqual([dict(Source='Local', Index=6, Operation=0, Value=0, Expected=True)], terrain['Guards'])


if __name__ == '__main__':
    unittest.main()

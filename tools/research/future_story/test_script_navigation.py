import os
from pathlib import Path
import subprocess
import sys
import unittest
from script_navigation import paths, compare, intervals, coordinate_locals, region_actions, guard, simplify


class ScriptNavigationTests(unittest.TestCase):
    def test_completed_helper_can_run_again_after_its_gate_changes(self):
        code = bytes.fromhex('5600100002020356011000020203001610010004ca010000')
        actions, complete = paths(code, [[0] * 16, [15] * 16], 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[])], actions)

    def test_active_recursive_request_remains_bounded(self):
        code = bytes.fromhex('020003ca010000')
        actions, complete = paths(code, [[0] * 16], 0, budget=100)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[])], actions)

    def test_join_cannot_suppress_a_request_visited_by_another_branch(self):
        # A shared code suffix is also an actor function. Earlier traversal of
        # that suffix is not an active recursive call. Both current paths must
        # still be able to request it after their branch histories rejoin.
        code = bytes.fromhex('1602010009ca0100160201000116010100020002020300')
        actions, complete = paths(code, [[0] * 16, [5] * 16], 0)
        self.assertTrue(complete)
        for first in (0, 1):
            for second in (0, 1):
                values = {1: first, 2: second}
                gives = any(a['Kind'] == 'Item' and all(
                    compare(values[g['Index']], g['Value'], g['Operation']) == g['Expected']
                    for g in a['Guards']) for a in actions)
                self.assertEqual(second == 1 or first != 1, gives)

    def test_actor_battle_keeps_controller_coordinate_approach_condition(self):
        # The controller continuously reads leader X into local06. Checking its
        # side of an object is a goal-position condition, not a live quest flag.
        code = bytes.fromhex('1206110204d8010000')
        actions, complete = paths(code, [], 0, initial_coordinates={6: 'X'})
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Encounter', Value=1, Guards=[guard('X', 6, 2, 17)])], actions)
        code = bytes.fromhex('1206110204ca010000')
        actions, complete = paths(code, [], 0, initial_coordinates={6: 'X'})
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[guard('X', 6, 2, 17)])], actions)

    def test_contact_battle_does_not_offer_automatic_post_battle_rewards(self):
        actions, complete = paths(bytes.fromhex('120901000200d80200ca010000'), [], 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Encounter', Value=2,
            Guards=[guard('Local', 9, 0, 1, False)])], actions)

    def test_contact_signal_connects_to_room_battle_with_remaining_guards(self):
        consumers = [dict(Kind='Encounter', Value=2, Controller=1,
            Guards=[guard('Global', 0, 5, 81), guard('Local', 10, 0, 1)])]
        actions, complete = paths(bytes.fromhex('750a00'), [], 0, signal_encounters=consumers)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Encounter', Value=2, Controller=1,
            Guards=[guard('Global', 0, 5, 81), guard('Local', 10, 1, 1)])], actions)

    def test_unrelated_or_wrong_value_signal_is_not_a_battle(self):
        consumers = [dict(Kind='Encounter', Value=2, Controller=1,
            Guards=[guard('Local', 10, 0, 1)])]
        for script in ('750b00', '770a00'):
            actions, complete = paths(bytes.fromhex(script), [], 0, signal_encounters=consumers)
            self.assertTrue(complete)
            self.assertEqual([], actions)

    def test_resetting_a_bypass_latch_does_not_claim_a_remote_battle(self):
        # Prison Passage71 resets local0B after a cutscene. The battle is
        # started by walking to another tile, not by resetting that latch.
        for guards in ([guard('Local', 11, 0, 1, False)],
                       [guard('Local', 11, 0, 1, False), guard('X', 7, 0, 14)]):
            consumers = [dict(Kind='Encounter', Value=2, Controller=1, Guards=guards)]
            actions, complete = paths(bytes.fromhex('770b750b00'), [], 0, signal_encounters=consumers)
            self.assertTrue(complete)
            self.assertEqual([], actions)

    def test_signal_keeps_coordinate_bypass_conditions_from_room_loop(self):
        # Scene146 actor12 at(7,21) sets08. Earlier controller branches for
        # other floor battles are bypassed before it reads08 and starts D8.
        consumers = [dict(Kind='Encounter', Value=0, Controller=0,
            Guards=[guard('Local', 8, 0, 1), guard('X', 7, 0, 23, False)])]
        actions, complete = paths(bytes.fromhex('750800'), [], 0, signal_encounters=consumers)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Encounter', Value=0, Controller=0,
            Guards=[guard('Local', 8, 1, 1), guard('X', 7, 0, 23, False)])], actions)

    def test_delegated_contact_signal_reaches_controller_battle(self):
        consumers = [dict(Kind='Encounter', Value=0, Controller=1,
            Guards=[guard('Local', 10, 0, 1)])]
        code = bytes.fromhex('04022300750a00')
        actions, complete = paths(code, [[0] * 16, [4] * 16], 0, signal_encounters=consumers)
        self.assertTrue(complete)
        self.assertTrue(any(a['Kind'] == 'Encounter' and a['Controller'] == 1 for a in actions))

    def test_async_callback_wait_does_not_retest_pre_contact_local_value(self):
        # Scene602 actors19..22 gate a battle request on local18==0. The
        # controller schedules callbacks that set its bits, then waits for15.
        # The later comparison is not another condition on the untouched frame.
        code = bytes.fromhex('12180000040202230002040312180f0404d800000063031800')
        actions, complete = paths(code, [[0] * 16, [9] * 16, [21] * 16], 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Encounter', Value=0,
            Guards=[guard('Local', 24, 0, 0)])], actions)

    def test_word_callback_write_does_not_change_the_next_local_operand(self):
        # PC locals are pairs at +118B0+index*8. Opcode76 writes that pair;
        # its high byte is not the next opcode12 operand's first byte.
        code = bytes.fromhex('120600000c0202231206010004d8000000760500')
        actions, complete = paths(code, [[0] * 16, [17] * 16], 0)
        self.assertTrue(complete)
        self.assertEqual([], actions)

    def test_spekkio_challenge_requires_completed_lesson_and_current_party_magic(self):
        from bonus_regions import actor_actions
        actions = actor_actions(465, 10)
        def offered(point, introduced=2, marle=False, magic=0):
            def value(g):
                return point if (g['Source'],g['Index']) == ('Global',0) else \
                    introduced if (g['Source'],g['Index']) == ('Global',0xe1) else \
                    magic if (g['Source'],g['Index']) == ('Global',0x1e0) else \
                    int(marle) if (g['Source'],g['Index']) == ('ActiveParty',1) else 0
            return any(a['Kind']=='Encounter' and all(compare(value(g),g['Value'],g['Operation'])==g['Expected']
                       for g in a['Guards']) for a in actions)
        self.assertTrue(offered(77))
        self.assertFalse(offered(76))
        self.assertFalse(offered(77, introduced=0))
        self.assertFalse(offered(77, marle=True))
        self.assertTrue(offered(77, marle=True, magic=2))

    def test_flea_confirm_signal_requires_the_visible_unfinished_phase(self):
        from bonus_regions import actor_actions
        actions = actor_actions(173, 11)
        self.assertEqual(1, len(actions))
        action = actions[0]
        self.assertEqual(('Encounter', False, 12, 0xc092),
                         (action['Kind'], action['Touch'], action['Controller'], action['Value']))
        def offered(keep=0x76, transformed=0, signal=0, ending=0):
            values = {('Global', 0xa3): keep, ('Global', 0x57): transformed, ('Local', 0xa): signal,
                      ('Global', 0xdf): ending}
            return all(compare(values[(g['Source'], g['Index'])], g['Value'], g['Operation']) == g['Expected']
                       for g in action['Guards'])
        self.assertTrue(offered())
        self.assertFalse(offered(keep=0x74))
        self.assertFalse(offered(keep=0x7e))
        self.assertFalse(offered(transformed=4))
        self.assertFalse(offered(signal=1))
        self.assertFalse(offered(ending=1))

    def test_delegated_menu_only_handler_is_an_interaction(self):
        code = bytes.fromhex('02020300c80000')
        actors = [[0] * 16, [4] * 16]
        actions, complete = paths(code, actors, 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Menu', Guards=[])], actions)

    def test_unread_writes_do_not_multiply_equivalent_interaction_states(self):
        # Blackbird restoration writes many save cells that no remaining gate
        # reads. Their possible combinations cannot change the final pickup.
        code = b''.join(bytes([0x16, i, 1, 0, 5, 0x56, 1, 100 + i, 0]) for i in range(12))
        code += bytes.fromhex('ca010000')
        actions, complete = paths(code, [], 0, budget=3000)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[])], actions)

    def test_a_write_read_by_a_delegated_condition_is_not_discarded(self):
        code = bytes.fromhex('56011000020203001610010005ca010000')
        actions, complete = paths(code, [[0] * 16, [8] * 16], 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[])], actions)

    def test_rejoined_unrelated_flags_do_not_exhaust_interaction_budget(self):
        # Twelve independent flag checks all rejoin before a pickup. The game
        # reaches it for every input; enumerating all 4,096 histories loses it.
        code = b''.join(bytes([0x16, index, 1, 0, 3, 0xe8, 0]) for index in range(12))
        code += bytes.fromhex('ca010000')
        actions, complete = paths(code, [], 0, budget=3000)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[])], actions)

    def test_join_keeps_branches_separate_when_their_local_effects_differ(self):
        # If global10 == 1, local06 becomes 1; otherwise it becomes 0.
        # Only the first path passes the local test and reaches the item.
        code = bytes.fromhex('16100100064f010610044f00061206010005ca010000')
        actions, complete = paths(code, [], 0)
        self.assertTrue(complete)
        self.assertEqual([dict(Kind='Item', Guards=[guard('Global', 0x10, 0, 1)])], actions)

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
        # A fresh leader read in a talk/contact handler restricts the approach
        # position too; it is not an old-frame scratch-cell availability test.
        actions, complete = paths(bytes.fromhex('22000406' + suffix), [], 0)
        self.assertTrue(complete)
        self.assertEqual('X', next(a for a in actions if a['Kind'] == 'Warp')['Guards'][0]['Source'])

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

"""Ocean Palace visions return to their trigger room, not the machine's exit.

Atel0220 actor9 enters415; Atel0223 actor13 function3 writes point195
at0xACC and returns404 at0xAE0. Atel0397 actor33 enters415; actor13
function4 writes198 at0xB4B and returns409 at0xB4D. These are progress
interactions, not traversable shortcuts through the Mammon Machine.
"""
from script_navigation import guard


def refine(entry):
    if entry['Id'] == 404:
        for region in entry['Regions']:
            if region['Actor'] == 9 and region['Kind'] == 'Warp' and region['Destination'] == 415:
                region.update(Kind='Progress', Destination=-1, Value=195)
                region['Guards'] += [guard('Global', 0, 3, 195),
                                     guard('Global', 0xF8, 6, 0x80, False)]
    if entry['Id'] == 409:
        for actor in entry['Actors']:
            if actor['Id'] == 33:
                for action in actor['Actions']:
                    if action['Kind'] == 'Warp' and action['Destination'] == 415:
                        action.update(Kind='Progress', Value=198)
                        for key in ('Destination', 'ArrivalX', 'ArrivalY'):
                            action.pop(key, None)
                        action['Guards'].append(guard('Global', 0, 3, 198))
                actor['Destinations'] = [a['Destination'] for a in actor['Actions'] if a['Kind'] == 'Warp']

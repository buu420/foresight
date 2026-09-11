"""Build the embedded footstep bank from Kenney's CC0 Impact Sounds 1.0.

Usage: py -3 tools/Prepare-Footsteps.py extracted-pack-directory output-directory
Requires ffmpeg on PATH. Source: https://kenney.nl/assets/impact-sounds
The source archive and complete sound library are not redistributed here.
"""
import array
import pathlib
import struct
import subprocess
import sys
import wave

RATE = 48000
source, output = map(pathlib.Path, sys.argv[1:3])
output.mkdir(parents=True, exist_ok=True)

def decode(name):
    result = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(source / 'Audio' / name),
        '-af', 'highpass=f=65,lowpass=f=7500', '-ac', '1', '-ar', str(RATE),
        '-f', 'f32le', '-'], check=True, capture_output=True)
    samples = array.array('f')
    samples.frombytes(result.stdout)
    return samples

def write(name, samples):
    with wave.open(str(output / name), 'wb') as stream:
        stream.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        stream.writeframes(b''.join(struct.pack('<h', round(max(-1, min(1, s)) * 32767)) for s in samples))

bank = []
for i in range(5):
    wood = decode(f'footstep_wood_{i:03}.ogg')
    sole = decode(f'footstep_carpet_{i:03}.ogg')
    samples = [0.7 * (wood[j] if j < len(wood) else 0) +
               0.35 * (sole[j] if j < len(sole) else 0)
               for j in range(min(round(RATE * .24), max(len(wood), len(sole))))]
    gain = (.24 + .008 * (i % 3)) / max(abs(s) for s in samples)
    samples = [s * gain * min(1, j / (RATE * .0015)) *
               min(1, (len(samples) - 1 - j) / (RATE * .018)) for j, s in enumerate(samples)]
    write(f'footstep-{i + 1}.wav', samples)
    bank.append(samples)

preview = [0.] * (RATE * 7)
for i, at in enumerate([.3, .94, 1.6, 2.23, 2.88, 3.9, 4.26, 4.63, 4.98, 5.35, 5.7, 6.08]):
    start = round(at * RATE)
    for j, value in enumerate(bank[i % len(bank)]):
        preview[start + j] += value
write('footstep-preview.wav', preview)
print(f'Wrote five 48 kHz footstep variations and a preview to {output}')

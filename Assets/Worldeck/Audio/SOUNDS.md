# Worldeck audio

All 82 files are synthesised by `Tools/make_sounds.py` (numpy + scipy; no samples; fixed seeds, so a rerun
gives identical files). 16-bit PCM mono WAV, 17.6 MB in total. Regenerate with `python Tools/make_sounds.py`.

**Levels.** Beds RMS -24 .. -20 dBFS (the spec's bed level; duck them -6 dB under speech). `fire_loop` and
`jukebox_music` RMS -20 dBFS. Every 48 kHz sound (UI, feedback, builds, taps, Ember, roasting, others, and the
`roast_sizzle` loop) peaks at -14 dBFS, except `ui_hover` -18 dBFS (it repeats a lot), `constellation` -16 dBFS
and `tide_wave` -12 dBFS. Key: every musical note (earcons, flourishes, chimes, beeps, bird whistles, the owl, the space hum
A1/A2/E3) is C major pentatonic (C D E G A); the jazz is diatonic C major. Crickets, gulls, bees, rain and fire keep
their natural pitches.

**In the app (Nov 16, "keep it simple": one thing in front at a time).** `Sfx.BedVolume` 0.3 (was 0.55);
under the calm music the bed drops to 35% of that (`Director.BedUnderMusic`), and comes back with the music off or in
the storm. `music_calm` plays at 0.16 (`Director.CalmMusicVolume`, was 0.22). The fire loop 0.18, 0.3 once built
(was 0.35, 0.6). Clips that are music themselves (`jukebox_music`, `amb_city_jazz`) silence the background music.

**Loops.** Continuous layers are rendered 1 s long and the overshoot is equal-power crossfaded into the head;
events and music are rendered circularly. Every loop is exactly periodic: the last sample runs into the first
like any two neighbouring samples. Set the AudioSources to loop; no crossfade is needed in Unity.

## Ambience beds (24 kHz, 24 s seamless loops)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `amb_forest_night.wav` | campfire forest at night: soft wind (band 220-700 Hz, gusts), leaf rustle, three crickets (4.25/4.5/4.75 kHz, 3-5 x 15 ms pulses every 0.6-1.1 s); no owl (it read as creepy on the headset) | 24.000 s | -7.2 | -22.0 |
| `amb_beach.wav` | tropical beach: three wave cycles (7.6/9.0/7.4 s; pink wash LP swept 400-3k Hz, crash body, foam hiss), light wind, two distant gull series (band-limited saw 1.4k->900 Hz) | 24.000 s | -3.2 | -21.0 |
| `amb_desert.wav` | desert dunes: gusty wind (250-900 Hz, Q 1.5), sand hiss on gusts, two occasional whistles (Q 20, 1.2-2 kHz), two sparse wind-chime clusters (rods C6-A6) | 24.000 s | -3.8 | -22.0 |
| `amb_snow.wav` | snowy cabin, very soft: dark wind (200-650 Hz), low buffeting, faint whistle (Q 20) swelling with gusts | 24.000 s | -7.0 | -23.5 |
| `amb_space.wav` | space station: 55/110/165 Hz hum with slow beating twins, vent noise (pink 150 Hz-1.8 kHz) + airflow band, three rare soft beeps (G5/A5/C6/D6/E6) | 24.000 s | -8.1 | -22.0 |
| `amb_reef.wav` | coral reef: brown-noise rumble LP 70 Hz + 41/55 Hz sines, surging wash LP 500 Hz, bubble streams and singles (rising sines 300-1200 Hz, 20-60 ms, LP 900 Hz) | 24.000 s | -8.7 | -22.0 |
| `amb_city_jazz.wav` | jazz club at 2 a.m. in the rain: rain on glass (Poisson ticks HP 900 Hz, gust-driven density) + drips, two distant wet-road car passes, and a jazz trio through the wall (LP 1.2 kHz): walking bass, brushes, Rhodes, ii-V-I in C (Dm9 G13 Cmaj9 x3) at 90 BPM | 24.000 s | -5.5 | -21.0 |
| `amb_meadow.wav` | spring meadow: breeze (350-1000 Hz) + fluttering leaves, seven bird phrases (FM chirp trills, C-pentatonic whistles, 'fee-bee'), three bee fly-bys (~230 Hz) and a faint distant hive hum | 24.000 s | -5.8 | -22.0 |

## Fire and jukebox (24 kHz seamless loops)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `fire_loop.wav` | campfire: roar (brown LP 150 Hz, flicker), lapping (~420 Hz band, 1-9 Hz flutter), hiss (HP 3 kHz, -24 dB), crackles (Poisson 4-12/s, 1-6 ms, BP 1.5-6 kHz) with occasional pops | 12.000 s | -5.2 | -20.0 |
| `jukebox_music.wav` | jazz trio, 90 BPM swung, 6 bars (Dm9, G13, Cmaj9, Am9, Dm9-G13, C6/9): Rhodes (FM), walking upright bass, brushes; LP 5.5 kHz (brighter than the bed's 1.2 kHz); starts half a beat before bar 1 | 16.000 s | -8.6 | -20.0 |
| `music_campfire.wav` | music by the fire: fingerpicked guitar (pluck), 60 BPM, G \| Em \| C \| D with a top-string melody, warm reverb | 16.000 s | -6.3 | -20.0 |
| `music_calm.wav` | calm, for every world and your room: 60 BPM, F major, Fmaj9 \| Dm9 \| Bbmaj9 \| C6sus x4, slow warm pads, a soft low root, a gentle electric-piano melody (major pentatonic) that changes each time round, rare high glockenspiel, long reverb; nothing sudden, no drums. Rendered alone by its LP entry in `make_sounds.py` | 64.000 s | -10.6 | -24.0 |
| `music_beach.wav` | island marimba, 120 BPM, C \| Am \| F \| G x2, low marimba bass, soft wood shaker on the off-beats | 16.000 s | -6.2 | -20.0 |
| `music_space.wav` | stars: slow pads Cmaj9 / Am9 / Fmaj9 / G6 (4 s each) and a sparse two-octaves-up glockenspiel, long reverb | 16.000 s | -7.6 | -21.0 |

## UI and feedback (48 kHz one-shots)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `ui_hover.wav` | 8 ms C8 (4.2 kHz) sine tick + faint C7 body | 0.035 s | -18.0 | -35.9 |
| `ui_select.wav` | bubble blip G5->C6 over a soft C5 kalimba body | 0.160 s | -14.0 | -28.9 |
| `grab.wav` | pluck gliding 660->880 Hz (E5->A5) + 120 Hz thump with a short pitch drop | 0.250 s | -14.0 | -29.0 |
| `place_1.wav` | wooden tock (G5 block) + marimba C5 (step 1 of C D E G A C) | 0.350 s | -14.0 | -26.4 |
| `place_2.wav` | wooden tock (G5 block) + marimba D5 (step 2 of C D E G A C) | 0.350 s | -14.0 | -27.6 |
| `place_3.wav` | wooden tock (G5 block) + marimba E5 (step 3 of C D E G A C) | 0.350 s | -14.0 | -27.1 |
| `place_4.wav` | wooden tock (G5 block) + marimba G5 (step 4 of C D E G A C) | 0.350 s | -14.0 | -24.5 |
| `place_5.wav` | wooden tock (G5 block) + marimba A5 (step 5 of C D E G A C) | 0.350 s | -14.0 | -27.5 |
| `place_6.wav` | wooden tock (G5 block) + marimba C6 (step 6 of C D E G A C) | 0.350 s | -14.0 | -26.8 |
| `build_complete.wav` | marimba+glock arpeggio C5 E5 G5 C6 (90 ms apart), then a 1.3 s shimmering tail: C-major pad, 16 random high glass pings, faint 7 kHz air | 1.700 s | -14.0 | -28.2 |
| `listen_start.wav` | soft bell tones G5 then C6, 85 ms apart (rising = mic open) | 0.300 s | -14.0 | -24.8 |
| `listen_stop.wav` | soft bell tones C6 then G5, 85 ms apart (falling = mic closed) | 0.300 s | -14.0 | -24.6 |
| `ack.wav` | C5+G5 soft bloom: 30 ms swell, brightness opens then closes | 0.380 s | -14.0 | -26.8 |
| `not_understood.wav` | two rounded low notes E4 -> C4, LP 1.1-1.3 kHz, never a buzzer | 0.380 s | -14.0 | -23.8 |
| `reveal_whoosh.wav` | pink noise band-pass (Q 1.3) sweeping 300->2000 Hz + an airy band an octave up, swell to 0.65 s then away | 1.000 s | -14.0 | -29.8 |
| `reveal_thump.wav` | soft thump settling 115 Hz -> E2 (82 Hz), LP noise puff, nine tiny crackles | 0.600 s | -14.0 | -25.9 |
| `world_end.wav` | descending chimes C6 A5 G5 E5 C5 + a C4/G4 bloom under the last note | 1.000 s | -14.0 | -24.8 |
| `souvenir_chime.wav` | C6 'major' bell (hum C5, fifth, octave, tenth, twelfth, slow beating) + faint G7 glass | 1.200 s | -14.0 | -27.6 |
| `touch.wav` | soft airy tick (HP 3.5 kHz noise, 4 ms) over a warm G3 body | 0.150 s | -14.0 | -28.0 |
| `item_appear.wav` | glass sparkle C7 E7 G7 A7 C8 E8 every 35 ms + soft C6 + 7.5 kHz air | 0.500 s | -14.0 | -27.7 |
| `fire_ignite.wav` | whoomp (pink noise LP opening 180->1600 Hz then closing) + low body + a crackle burst thinning from 50/s to 5/s | 1.200 s | -14.0 | -32.3 |
| `wisp_hello.wav` | gentle two-note glass chime E6 -> A6 (110 ms apart) | 0.390 s | -14.0 | -26.5 |

## Per-world build flourishes (48 kHz one-shots, at most 2 s)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `build_fire.wav` | warm kalimba C4 E4 G4 C5 E5 + C-major pad bloom, a soft flame whoomp and a few crackles | 1.600 s | -14.0 | -29.3 |
| `build_sandcastle.wav` | playful marimba C5 E5 G5 E5 A5 C6, sand swishes, two pats and a glock C7 sparkle | 1.500 s | -14.0 | -26.8 |
| `build_crystal.wav` | glass arpeggio C6 E6 G6 C7 E7 G7, beating C6/G6 shimmer pad, ten high glints | 1.600 s | -14.0 | -30.5 |
| `build_snowman.wav` | three soft snow pats, celesta E5 G5 C6 (+E6 G6), tiny sleigh-bell glints | 1.600 s | -14.0 | -28.1 |
| `build_starmap.wav` | soft blips in stacked fifths C5 G5 D6 A6 E7 with echoes, C4/G4/D5 pad and C3 sub swell, faint twinkles | 1.800 s | -14.0 | -30.4 |
| `build_anemone.wav` | watery notes C5 D5 E5 G5 A5 that wobble into pitch, bubbles, C/E/A pad; LP 3 kHz | 1.600 s | -14.0 | -28.3 |
| `build_jukebox.wav` | walking-bass pickup G2 A2 B2 -> C2 with a Cmaj9 Rhodes chord, D5 E5 fill, brush taps and swish | 1.800 s | -14.0 | -26.2 |
| `build_blossom.wav` | harp gliss C5..G6 (pentatonic), petal glass pings, a breeze swell and two little bird chirps | 1.800 s | -14.0 | -31.4 |

## Table taps, one per world (48 kHz, at most 0.35 s)

Alternate `tap_city_1` (kick) and `tap_city_2` (snare) like a drummer.

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `tap_campfire.wav` | soft log knock (C4 wood modes, small pitch drop) + four spark crackles 2-5 kHz | 0.300 s | -14.0 | -32.2 |
| `tap_beach.wav` | sand puff: pink noise LP 1.8 kHz (5 ms attack) + sparse grains above 2.5 kHz + a muffled thud | 0.300 s | -14.0 | -32.1 |
| `tap_desert.wav` | dry sand tick (2.5-9 kHz, 2.5 ms) + six trickling grains + a tiny E7 rod chime | 0.300 s | -14.0 | -27.5 |
| `tap_snow.wav` | snow crunch: 55 tiny grains front-loaded over 70 ms, a 25-grain settle, packed-snow thud | 0.250 s | -14.0 | -32.5 |
| `tap_space.wav` | console blip E6 (flicks up from just below) over a small metal-plate tick | 0.220 s | -14.0 | -27.4 |
| `tap_reef.wav` | water bloop: bubble C4->C5 + a small E5->A5 bubble + soft plunk | 0.280 s | -14.0 | -28.3 |
| `tap_city_1.wav` | feathered jazz kick settling 95 Hz -> A1 (55 Hz), felt beater, a breath of brush (alternate with tap_city_2) | 0.300 s | -14.0 | -25.2 |
| `tap_city_2.wav` | brushed snare: three-bristle brush slap (BP 3.2 kHz), G3 drum body, soft wire rattle | 0.300 s | -14.0 | -32.6 |
| `tap_meadow.wav` | small wooden tick (C6 block) + a tiny E7->G7 bird chirp | 0.200 s | -14.0 | -31.1 |

## Ember, the guide light (48 kHz, wordless creature voice)

`ember_chirp_1..6` are single syllables (60-110 ms) to play one per caption syllable; pick them in a shuffled
order so no two neighbours repeat.

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `ember_chirp_1.wav` | Ember syllable 'pi' rising E5->G5 (creature voice: harmonics through morphing vowel formants) | 0.075 s | -14.0 | -19.1 |
| `ember_chirp_2.wav` | Ember syllable 'mu' falling A5->E5 (creature voice: harmonics through morphing vowel formants) | 0.090 s | -14.0 | -21.6 |
| `ember_chirp_3.wav` | Ember syllable 'ba' arc D5->A5->G5 (creature voice: harmonics through morphing vowel formants) | 0.100 s | -14.0 | -20.0 |
| `ember_chirp_4.wav` | Ember syllable 'ti' short high C6->A5 (creature voice: harmonics through morphing vowel formants) | 0.060 s | -14.0 | -22.6 |
| `ember_chirp_5.wav` | Ember syllable 'wo' low lift C5->D5 (creature voice: harmonics through morphing vowel formants) | 0.110 s | -14.0 | -21.0 |
| `ember_chirp_6.wav` | Ember syllable 'ne' tilt G5->A5->E5 (creature voice: harmonics through morphing vowel formants) | 0.080 s | -14.0 | -20.7 |
| `ember_giggle.wav` | three breathy 'hee's rising E5, G5, A5 (each flicks down a step), 105 ms apart | 0.350 s | -14.0 | -22.3 |
| `ember_wave.wav` | happy 'hi-ya!': G5->A5, then A5 up to a held C6 with vibrato | 0.380 s | -14.0 | -23.8 |
| `ember_yawn.wav` | slow yawn: lifts G5->A5 then glides down to C5, vowel 'ah'->'oo', breathy, vibrato | 0.800 s | -14.0 | -23.0 |
| `ember_hello.wav` | excited 'he-llo!': E5->G5, then a G5->C6->A5 arc with vibrato and a C7 E7 G7 sparkle | 0.500 s | -14.0 | -22.2 |

## Campfire roasting (48 kHz)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `roast_sizzle.wav` | soft marshmallow sizzle, 48 kHz 4 s seamless loop: 4 kHz noise band (LP 6 kHz) with a 5-30 Hz frying flutter, dense micro-pops (~140/s, 2.4-5.5 kHz) and a few small pops; peak -14 dBFS | 4.000 s | -14.0 | -30.3 |
| `golden_chime.wav` | bright strum E6 G6 C7 (glock) over a soft C6 kalimba + G7/E8 glints: the marshmallow is perfect | 0.850 s | -14.0 | -27.1 |
| `marshmallow_ignite.wav` | small whoomp (pink noise LP opening 300->2500 Hz, closing to 500) + tiny body + four crackles | 0.600 s | -14.0 | -32.4 |
| `extinguish.wav` | puff (pink LP 700 Hz, 60 ms) + steam hiss sliding 5.5 -> 3.2 kHz + three last sputters | 0.500 s | -14.0 | -30.9 |
| `crunch.wav` | eating: three bites of granular crunch (40/30/22 grains, 1-4.5 kHz) with a soft low chomp each | 0.300 s | -14.0 | -34.8 |
| `stoke_whoosh.wav` | palm pumps air: breathy band 400->1100->600 Hz + the fire's roar swelling (brown LP 300 Hz) + four crackles | 0.400 s | -14.0 | -27.3 |
| `sparks_rise.wav` | 26 glass pings climbing C6 -> C8 as the sparks rise and fade, an airy band rising 2 -> 7 kHz, an initial crackle | 2.000 s | -14.0 | -28.8 |
| `constellation.wav` | warm C6/9 pad (C3 G3 E4 A4 D5) blooming open (LP 900 -> 3000 Hz over 1.6 s) + C3 sub + eight faint star pings; peak -16 dBFS | 3.500 s | -16.0 | -28.7 |

## Others (48 kHz)

| file | what it is | length | peak dBFS | RMS dBFS |
|---|---|---|---|---|
| `visitor_land.wav` | soft flutter: five wing flaps (band ~700 Hz) slowing and fading, then a tiny settle | 0.400 s | -14.0 | -35.0 |
| `bloom.wav` | a soft tone growing C5->G5 as its brightness opens, kalimba plinks C5..E6 speeding up, rising air, C7 E7 G7 sparkle | 1.200 s | -14.0 | -26.4 |
| `tide_wave.wav` | big wave arriving: pink wash swelling as its LP opens 300->1.5k Hz, crash at 1.5 s (3.5 kHz, low body), foam hiss out; peak -12 dBFS | 2.500 s | -12.0 | -28.7 |
| `fish_dart.wav` | quick swish: band (Q 1.8) flicking 700->3200->1500 Hz + two small bubbles; LP 5 kHz | 0.250 s | -14.0 | -28.7 |
| `power_on.wav` | two relay ticks, A1/A2/E3 hum fading in, a soft tone sweeping C3->C5 as it brightens, 'ready' G5 -> C6 chime | 1.500 s | -14.0 | -27.7 |
| `simon_1.wav` | clean C5 tone for the memory game (sine + faint 2nd/3rd harmonics, 6 ms attack, released at 0.2 s) | 0.250 s | -14.0 | -20.8 |
| `simon_2.wav` | clean D5 tone for the memory game (sine + faint 2nd/3rd harmonics, 6 ms attack, released at 0.2 s) | 0.250 s | -14.0 | -20.6 |
| `simon_3.wav` | clean E5 tone for the memory game (sine + faint 2nd/3rd harmonics, 6 ms attack, released at 0.2 s) | 0.250 s | -14.0 | -20.0 |
| `simon_4.wav` | clean G5 tone for the memory game (sine + faint 2nd/3rd harmonics, 6 ms attack, released at 0.2 s) | 0.250 s | -14.0 | -19.9 |
| `simon_5.wav` | clean A5 tone for the memory game (sine + faint 2nd/3rd harmonics, 6 ms attack, released at 0.2 s) | 0.250 s | -14.0 | -20.3 |
| `wrong_soft.wav` | gentle 'hmm-mm' not-quite: rounded tone gliding E5 -> C5 with two soft wobbles, LP 1.5 kHz | 0.300 s | -14.0 | -21.1 |

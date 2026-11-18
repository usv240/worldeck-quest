// Hand-written recipes for places a demo, a judge or a first-time player is likely to say, and
// that the app's offline parser (Words.cs) does not already understand. A hit is answered with
// source "seed" and no model call, so these work even with no API key and cost nothing.
//
// Each entry: say = the phrasings that pick it (matched after seedKey below), r = the recipe.
// Every recipe must already be inside the kit's ranges (the tests check that clampRecipe leaves
// each one unchanged) and every title must be generic: no brand, franchise or real person names.

import { normalise } from "./kit.js";

const R = (biome, hour, precip, fog, wind, density, warmth, light, extras, title) =>
  ({ biome, hour, precip, fog, wind, density, warmth, light, extras, title });

export const SEEDS = [
  // ---- the twenty worked examples in the system prompt ----
  { say: ["castle", "medieval castle", "old castle", "fairy tale castle", "fairytale castle", "castle on a hill", "palace", "royal palace"],
    r: R("meadow", 19, "none", 0.15, 0.3, 0.9, 0.3, 0, ["lanterns", "clouds"], "Old castle grounds at dusk") },
  { say: ["mars", "planet mars", "surface of mars", "red planet", "mars colony", "mars base"],
    r: R("desert", 15, "none", 0.3, 0.6, 0.4, 0.8, -0.1, [], "Red desert planet") },
  { say: ["tokyo", "tokyo at night", "neon city", "neon tokyo", "shibuya", "cyberpunk city"],
    r: R("city", 23, "rain", 0.2, 0.2, 1.3, -0.2, 0.1, ["lanterns"], "Neon city in the rain") },
  { say: ["haunted house", "haunted mansion", "haunted manor", "spooky house", "ghost house", "haunted castle"],
    r: R("campfire", 23.5, "none", 0.8, 0.5, 1.2, -0.6, -0.5, ["lanterns"], "Haunted house in the misty woods") },
  { say: ["rainforest", "rain forest", "tropical rainforest", "amazon rainforest", "amazon"],
    r: R("campfire", 11, "rain", 0.5, 0.2, 1.6, 0.4, -0.1, ["flowers", "clouds"], "Steamy rainforest in the rain") },
  { say: ["aquarium", "big aquarium", "aquarium tunnel", "giant fish tank", "fish tank"],
    r: R("reef", 12, "bubbles", 0.3, 0, 1.3, 0, 0.1, ["lanterns"], "Glowing aquarium tunnel") },
  { say: ["northern lights", "aurora borealis", "aurora", "lapland", "northern lights in lapland"],
    r: R("snow", 23, "none", 0, 0.1, 0.8, -0.4, -0.2, ["aurora", "shooting_stars"], "Northern lights over the snow") },
  { say: ["cozy library", "cosy library", "library", "old library", "reading room", "reading nook", "bookshop", "book shop"],
    r: R("snow", 20, "snow", 0.1, 0.1, 1.2, 0.8, -0.2, ["lanterns"], "Cozy library, snow outside") },
  { say: ["volcano", "active volcano", "erupting volcano", "volcano at night", "lava", "lava field"],
    r: R("desert", 21, "none", 0.4, 0.4, 0.5, 1, -0.3, ["embers"], "Volcano slopes at night") },
  { say: ["moon base", "moonbase", "lunar base", "base on the moon", "moon colony", "lunar colony"],
    r: R("space", 0, "none", 0, 0, 0.8, -0.3, 0, ["shooting_stars", "crystals"], "Moon base under the stars") },
  { say: ["christmas", "christmas eve", "christmas village", "christmas market", "santa's workshop", "santas workshop", "winter wonderland"],
    r: R("snow", 19, "snow", 0.1, 0.2, 1.2, 0.7, 0.1, ["lanterns"], "Snowy Christmas eve") },
  { say: ["halloween", "halloween night", "pumpkin patch", "trick or treating"],
    r: R("campfire", 21, "none", 0.6, 0.4, 1.1, 0.5, -0.3, ["lanterns", "embers"], "Halloween night in the woods") },
  { say: ["ocean liner", "old ocean liner", "cruise ship", "cruise", "deck of a ship", "ship deck"],
    r: R("beach", 20, "none", 0.1, 0.5, 0.4, 0.2, -0.1, ["lanterns", "clouds"], "Ocean liner deck in the evening") },
  { say: ["ski lodge", "ski resort", "ski chalet", "chalet", "apres ski"],
    r: R("snow", 16.5, "snow", 0.1, 0.3, 1.1, 0.5, 0.1, ["lanterns"], "Ski lodge in the afternoon snow") },
  { say: ["zen garden", "japanese garden", "rock garden", "temple garden", "meditation garden"],
    r: R("meadow", 7, "none", 0.3, 0.05, 0.5, 0.1, 0, ["flowers", "lanterns"], "Quiet zen garden at dawn") },
  { say: ["savanna", "savannah", "safari", "african savanna", "serengeti", "african plains"],
    r: R("desert", 18, "none", 0.15, 0.3, 0.6, 0.7, 0.1, ["clouds"], "Golden savanna at sunset") },
  { say: ["venice", "venice canals", "canals of venice", "gondola", "gondola ride"],
    r: R("city", 19, "none", 0.2, 0.1, 1, 0.5, 0, ["lanterns"], "Venice canals at sunset") },
  { say: ["arcade", "retro arcade", "video arcade", "game arcade", "amusement arcade"],
    r: R("city", 22, "none", 0.1, 0, 1.4, -0.3, 0.3, ["lanterns", "crystals"], "Neon arcade at night") },
  { say: ["cathedral", "gothic cathedral", "church", "old church", "chapel", "abbey"],
    r: R("desert", 7, "none", 0.35, 0, 0.8, 0.3, 0.2, ["lanterns"], "Sunlit stone cathedral") },
  { say: ["cave", "caves", "crystal cave", "cavern", "underground cave", "grotto"],
    r: R("desert", 22, "none", 0.5, 0, 1.2, -0.4, -0.5, ["crystals", "fireflies"], "Glowing crystal cave") },

  // ---- famous fictional places, built as their generic versions ----
  { say: ["hogwarts", "hogwarts castle", "wizard school", "school of magic", "magic school", "wizard castle"],
    r: R("snow", 21.5, "snow", 0.25, 0.25, 1.1, 0.1, -0.1, ["lanterns"], "Castle-town in the snow at night") },
  { say: ["narnia", "lamp post in the snow", "snowy lamp post"],
    r: R("snow", 17, "snow", 0.2, 0.2, 1.2, 0.2, -0.1, ["lanterns"], "Lamp-lit snowy woods") },
  { say: ["tatooine", "desert planet", "twin suns", "two suns"],
    r: R("desert", 18.5, "none", 0.2, 0.4, 0.5, 0.6, 0, [], "Desert planet at double sunset") },
  { say: ["star wars", "star destroyer", "galaxy far far away", "millennium falcon"],
    r: R("space", 0, "none", 0, 0, 1.1, -0.2, 0, ["shooting_stars", "lanterns"], "Starship bridge above a far planet") },
  { say: ["wonderland", "alice in wonderland", "mad hatter's tea party", "tea party in wonderland"],
    r: R("meadow", 14, "none", 0, 0.2, 1.4, 0.3, 0.2, ["flowers", "crystals"], "Curious tea-party garden") },
  { say: ["jurassic park", "jurassic world", "dinosaurs", "dinosaur world", "dinosaur land", "prehistoric jungle"],
    r: R("campfire", 13, "none", 0.4, 0.3, 1.6, 0.5, 0, [], "Prehistoric jungle") },
  { say: ["dragon's lair", "dragons lair", "dragon lair", "dragon cave", "dragon"],
    r: R("desert", 22, "none", 0.4, 0.3, 0.7, 1, -0.4, ["embers", "crystals"], "Dragon's lair in the canyon") },

  // ---- other likely asks ----
  { say: ["enchanted forest", "fairy forest", "magic forest", "magical forest", "fairy woods", "elf forest"],
    r: R("campfire", 21, "none", 0.4, 0.2, 1.4, 0.2, 0, ["fireflies", "flowers", "crystals"], "Enchanted forest with fireflies") },
  { say: ["lighthouse", "lighthouse on the coast", "lighthouse keeper's cottage"],
    r: R("beach", 20.5, "none", 0.4, 0.6, 0.5, -0.2, -0.1, ["lanterns", "clouds"], "Misty lighthouse coast") },
  { say: ["pirate cove", "pirate ship", "pirates", "pirate island", "treasure island"],
    r: R("beach", 18.5, "none", 0.1, 0.6, 1, 0.3, 0, ["lanterns", "clouds"], "Pirate cove at sunset") },
  { say: ["shipwreck", "sunken ship", "sunken treasure", "underwater shipwreck"],
    r: R("reef", 12, "bubbles", 0.5, 0, 0.9, -0.2, -0.3, ["lanterns", "crystals"], "Sunken shipwreck") },
  { say: ["underwater city", "city under the sea", "sunken city", "underwater palace"],
    r: R("reef", 12, "bubbles", 0.3, 0, 1.2, 0, 0.2, ["lanterns", "crystals"], "Glowing city under the sea") },
  { say: ["bamboo forest", "bamboo grove", "bamboo"],
    r: R("meadow", 8, "none", 0.4, 0.1, 1.5, 0.1, 0, [], "Misty bamboo grove") },
  { say: ["hot springs", "hot spring", "onsen", "thermal springs"],
    r: R("snow", 18, "snow", 0.6, 0.1, 1, 0.6, 0, ["lanterns"], "Steaming hot springs in the snow") },
  { say: ["vineyard", "vineyards", "tuscany", "tuscan hills", "wine country"],
    r: R("meadow", 18.5, "none", 0.1, 0.2, 1.1, 0.6, 0.1, ["clouds"], "Hillside vineyard at sunset") },
  { say: ["lavender fields", "lavender field", "lavender", "provence"],
    r: R("meadow", 17, "none", 0, 0.3, 1.3, 0.3, 0.1, ["flowers"], "Lavender fields in the afternoon") },
  { say: ["black hole", "wormhole", "edge of the universe"],
    r: R("space", 0, "none", 0, 0, 0.5, -0.4, -0.4, ["shooting_stars", "crystals"], "Edge of a black hole") },
  { say: ["treehouse", "tree house", "treehouse in the woods"],
    r: R("campfire", 20, "none", 0.1, 0.2, 1.3, 0.4, 0, ["lanterns", "fireflies"], "Treehouse on a summer evening") },
  { say: ["new orleans", "french quarter", "jazz street", "mardi gras"],
    r: R("city", 22, "none", 0.1, 0.1, 1.2, 0.6, 0.1, ["lanterns"], "Jazz street at night") },
  { say: ["las vegas", "vegas"],
    r: R("city", 23, "none", 0, 0.1, 1.4, 0.2, 0.4, ["lanterns", "crystals"], "Neon strip at night") },
  { say: ["hong kong", "hong kong harbour", "victoria harbour", "harbour city"],
    r: R("city", 21, "rain", 0.3, 0.3, 1.4, -0.1, 0.2, ["lanterns"], "Harbour city in the rain") },
  { say: ["santorini", "greek island", "greek islands", "greece", "mykonos", "mediterranean"],
    r: R("beach", 18.5, "none", 0, 0.3, 0.8, 0.4, 0.2, ["clouds"], "Whitewashed island at sunset") },
  { say: ["autumn", "autumn forest", "autumn woods", "autumn leaves", "fall", "fall foliage"],
    r: R("campfire", 15, "none", 0.1, 0.4, 1.1, 0.7, 0.1, [], "Autumn forest afternoon") },
  { say: ["above the clouds", "in the clouds", "cloud kingdom", "sky castle", "castle in the sky", "floating island", "heaven"],
    r: R("meadow", 12, "none", 0.5, 0.3, 0.8, 0.1, 0.6, ["clouds", "flowers"], "Garden above the clouds") },
  { say: ["jungle temple", "lost temple", "ancient temple", "temple ruins", "lost city", "mayan temple"],
    r: R("campfire", 10, "none", 0.4, 0.2, 1.5, 0.5, 0, ["flowers"], "Overgrown temple in the jungle") },
  { say: ["ice cave", "ice palace", "ice castle", "frozen palace", "glacier cave"],
    r: R("snow", 13, "none", 0.3, 0.1, 0.9, -0.8, -0.1, ["crystals"], "Blue ice cave") },
];

/** The calm world every decline gets, whatever the model said. */
export const CALM_RECIPE = Object.freeze(
  R("meadow", 10, "none", 0, 0.2, 1, 0.2, 0.1, ["flowers", "clouds"], "Flower meadow in spring"),
);

// Polite openers and fillers people put in front of a place ("computer, take me to a castle").
const LEAD = [
  "i want to go to", "i wanna go to", "i want to be in", "i want to be at", "i'd like to go to", "id like to go to",
  "turn the room into", "turn this into", "turn it into", "change it to", "let's go to", "lets go to",
  "take me to", "bring me to", "put me in", "put me on", "somewhere like", "can you", "could you",
  "take me", "let's go", "lets go", "go to", "i'd like", "id like", "i want", "show me", "make it", "make this",
  "build me", "build", "create", "make", "computer", "please", "hey", "okay", "ok", "um", "uh", "so", "now",
  "a", "an", "the", "some", "my", "to", "into", "in", "at", "on",
];
const TAIL = ["please", "now", "for me"];

/** The cache and seed key: Words.Normalise, then openers, articles and a trailing "please" removed. */
export function seedKey(text) {
  let t = normalise(text);
  for (let changed = true; changed;) {
    changed = false;
    for (const p of LEAD) {
      if (t === p) { t = ""; changed = true; break; }
      if (t.startsWith(p + " ")) { t = t.slice(p.length + 1); changed = true; break; }
    }
    for (const p of TAIL) {
      if (t.endsWith(" " + p)) { t = t.slice(0, -(p.length + 1)); changed = true; break; }
    }
  }
  return t;
}

/** seedKey(phrase) → recipe. Throws at load if two different seeds claim the same phrase. */
export const SEED_MAP = (() => {
  const map = new Map();
  for (const s of SEEDS) {
    for (const phrase of s.say) {
      const k = seedKey(phrase);
      if (!k) throw new Error(`seed phrase "${phrase}" is empty after seedKey`);
      const prev = map.get(k);
      if (prev && prev !== s.r) throw new Error(`seed phrase "${phrase}" is claimed twice`);
      map.set(k, s.r);
    }
  }
  return map;
})();

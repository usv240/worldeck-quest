// The kit vocabulary, MIRRORED FROM THE APP:
//   Assets/Worldeck/Scripts/Core/Kit.cs     (worlds, extras, precip, each world's Words)
//   Assets/Worldeck/Scripts/Core/Recipe.cs  (ranges and Clamp / FromJson rules)
//   Assets/Worldeck/Scripts/Core/Words.cs   (Normalise, BestBiome, CleanTitle, Blocked)
// If you change any of those files, change this one too. test/worker.test.js compares this
// file with the C# sources and fails when they drift apart.

export const KIT_VERSION = 1;

export const PRECIP = ["none", "rain", "snow", "bubbles"];

export const EXTRAS = ["aurora", "fireflies", "shooting_stars", "lanterns", "flowers", "crystals", "clouds", "embers"];

export const MIN_DENSITY = 0.3;
export const MAX_DENSITY = 1.6;
export const MAX_EXTRAS = 4;
export const MAX_TITLE = 40;

export const BIOMES = [
  {
    id: "campfire", name: "Campfire under the stars", short: "Forest", defaultHour: 22, hasTime: true,
    defaultPrecip: "none", defaultFog: 0, defaultExtras: ["fireflies"],
    words: ["campfire", "camp", "camping", "forest", "woods", "wood", "woodland", "trees", "tree", "pine", "cabin in the woods", "fire", "bonfire", "wilderness", "jungle"],
  },
  {
    id: "beach", name: "Tropical beach at sunset", short: "Beach", defaultHour: 18.5, hasTime: true,
    defaultPrecip: "none", defaultFog: 0, defaultExtras: ["clouds"],
    words: ["beach", "tropical", "island", "ocean", "sea", "seaside", "shore", "coast", "sand", "palm", "palms", "hawaii", "caribbean", "surf", "lagoon", "bay", "waves"],
  },
  {
    id: "desert", name: "Desert dunes at dawn", short: "Desert", defaultHour: 6.5, hasTime: true,
    defaultPrecip: "none", defaultFog: 0, defaultExtras: [],
    words: ["desert", "dune", "dunes", "sahara", "sand dunes", "oasis", "egypt", "pyramid", "pyramids", "canyon", "mars", "arizona", "mesa", "cactus"],
  },
  {
    id: "snow", name: "Snowy mountain cabin", short: "Snow", defaultHour: 15, hasTime: true,
    defaultPrecip: "snow", defaultFog: 0, defaultExtras: [],
    words: ["snow", "snowy", "winter", "mountain", "mountains", "alps", "arctic", "ski", "skiing", "cabin", "igloo", "ice", "frozen", "glacier", "christmas", "north pole", "tundra"],
  },
  {
    id: "space", name: "Space station in orbit", short: "Space", defaultHour: 0, hasTime: false,
    defaultPrecip: "none", defaultFog: 0, defaultExtras: ["shooting_stars"],
    words: ["space", "station", "spaceship", "starship", "ship", "orbit", "moon", "planet", "galaxy", "rocket", "cosmos", "stars", "astronaut", "nebula", "spacecraft", "sci-fi", "scifi", "future"],
  },
  {
    id: "reef", name: "Coral reef underwater", short: "Reef", defaultHour: 12, hasTime: false,
    defaultPrecip: "bubbles", defaultFog: 0, defaultExtras: [],
    words: ["underwater", "under water", "reef", "coral", "aquarium", "submarine", "ocean floor", "deep sea", "fish", "atlantis", "mermaid", "scuba", "diving", "sea floor"],
  },
  {
    id: "city", name: "Jazz club at 2 a.m.", short: "City", defaultHour: 2, hasTime: true,
    defaultPrecip: "rain", defaultFog: 0, defaultExtras: ["lanterns"],
    words: ["city", "jazz", "bar", "club", "nightclub", "rooftop", "downtown", "new york", "tokyo", "paris", "london", "neon", "street", "lounge", "speakeasy", "cafe", "restaurant", "music", "blues", "rain city"],
  },
  {
    id: "meadow", name: "Flower meadow in spring", short: "Meadow", defaultHour: 10, hasTime: true,
    defaultPrecip: "none", defaultFog: 0, defaultExtras: ["flowers", "clouds"],
    words: ["meadow", "garden", "park", "field", "fields", "flowers", "flower", "picnic", "countryside", "spring", "farm", "grass", "valley", "hills", "zen", "japan", "cherry blossom", "nature"],
  },
];

export const BIOME_IDS = BIOMES.map((b) => b.id);

const BY_ID = new Map(BIOMES.map((b) => [b.id, b]));

/** The world with this id, or null. */
export function biome(id) {
  return BY_ID.get(id) || null;
}

// Words.WeatherWords: these count less when picking a world.
export const WEATHER_WORDS = new Set(["snow", "snowy", "ice", "frozen", "winter", "rain city", "stars", "fire", "waves", "sand", "spring", "music"]);

// Words.Blocked: a title containing any of these (spaces removed) is dropped.
export const BLOCKED = [
  "fuck", "shit", "bitch", "cunt", "pussy", "asshole", "bastard", "whore", "slut", "nigg",
  "faggot", "retard", "porn", "nazi", "suicide", "penis", "vagina", "boobs",
];

// Words.BlockedWords: short words that hide inside innocent ones ("grape"), blocked only as whole words.
export const BLOCKED_WORDS = ["rape", "raped", "rapist", "tits", "tit", "cum", "sex", "sexy", "kill", "killing"];

const LETTER_OR_DIGIT = /[\p{L}\p{Nd}]/u;
const WHITESPACE = /\s/u;

/** Words.Normalise: lower case, a.m./p.m. joined, everything but letters, digits, ' and : becomes a space. */
export function normalise(text) {
  if (text === null || text === undefined || text === "") return "";
  let t = String(text).toLowerCase();
  t = t.replaceAll("a.m.", "am").replaceAll("p.m.", "pm").replaceAll("a. m.", "am").replaceAll("p. m.", "pm");
  t = t.replaceAll("’", "'");
  let out = "";
  for (const c of t) out += LETTER_OR_DIGIT.test(c) || c === "'" || c === ":" ? c : " ";
  return out.replace(/\s+/g, " ").trim();
}

/** Words.BestBiome: the kit world the words point at, or null. Longer phrases count more; weather words less. */
export function bestBiome(text) {
  const t = " " + normalise(text) + " ";
  let best = 0;
  let pick = null;
  let tie = false;
  for (const b of BIOMES) {
    let s = 0;
    for (const w of b.words) {
      if (!t.includes(" " + w + " ")) continue;
      let weight = w.includes(" ") ? 1.5 : 1;
      if (WEATHER_WORDS.has(w)) weight = 0.45;
      s += weight;
    }
    if (s > best + 1e-4) { best = s; pick = b.id; tie = false; }
    else if (s > 0 && Math.abs(s - best) < 1e-4) tie = true;
  }
  return tie && best < 1.5 ? null : pick;
}

/**
 * Words.CleanTitle: letters, digits, spaces and light punctuation, one line, at most 40 characters.
 * Empty if it contained anything blocked (the caller then uses the world's own name).
 */
export function cleanTitle(s) {
  if (typeof s !== "string" || s.length === 0) return "";
  let sb = "";
  for (const c of s) {
    if (LETTER_OR_DIGIT.test(c) || " '-,.&:!".includes(c)) sb += c;
    else if (WHITESPACE.test(c)) sb += " ";
  }
  let t = sb.replace(/\s+/g, " ").trim();
  const low = t.toLowerCase().replaceAll(" ", "");
  for (const b of BLOCKED) if (low.includes(b)) return "";
  for (const w of normalise(t).split(" ")) if (BLOCKED_WORDS.includes(w)) return "";
  if (t.length > MAX_TITLE) t = t.slice(0, MAX_TITLE).replace(/[ ,-]+$/, "");
  return t;
}

/** Recipe.Allowed: reef and space only take the extras that make sense there. */
export function allowedExtra(biomeId, extra) {
  if (biomeId === "reef") return extra === "lanterns" || extra === "crystals" || extra === "flowers";
  if (biomeId === "space") return extra === "shooting_stars" || extra === "crystals" || extra === "lanterns";
  return true;
}

/** Recipe.For: a world at its own defaults (wind 0.3, density 1, warmth 0, light 0). */
export function recipeFor(id) {
  const def = biome(id) || BIOMES[0];
  return clampFields({
    biome: def.id, hour: def.defaultHour, precip: def.defaultPrecip, fog: def.defaultFog, wind: 0.3,
    density: 1, warmth: 0, light: 0, extras: [...def.defaultExtras], title: def.name,
  });
}

// Json.Num: only a real, finite number counts; anything else takes the fallback.
const num = (v, fallback) => (typeof v === "number" && Number.isFinite(v) ? v : fallback);
const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
const tidy = (v) => {
  const r = Math.round(v * 1000) / 1000;
  return Object.is(r, -0) ? 0 : r;
};

/** Recipe.Clamp on a plain object whose fields are already the right types. */
function clampFields(r) {
  const def = biome(r.biome) || BIOMES[0];
  r.biome = def.id;
  let hour = Number.isFinite(r.hour) ? r.hour : def.defaultHour;
  hour = ((hour % 24) + 24) % 24;
  let precip = PRECIP.includes(r.precip) ? r.precip : "none";
  // Underwater the only "weather" is bubbles; above water there are no bubbles; space has none.
  if (def.id === "reef") precip = "bubbles";
  else if (precip === "bubbles") precip = "none";
  if (def.id === "space" && precip !== "none") precip = "none";
  const seen = new Set();
  const extras = [];
  for (const e of r.extras) {
    if (!EXTRAS.includes(e) || seen.has(e) || !allowedExtra(def.id, e)) continue;
    seen.add(e);
    extras.push(e);
  }
  let title = cleanTitle(r.title);
  if (title.length === 0) title = def.name;
  return {
    biome: def.id,
    hour: tidy(hour),
    precip,
    fog: tidy(clamp(r.fog, 0, 1)),
    wind: tidy(clamp(r.wind, 0, 1)),
    density: tidy(clamp(r.density, MIN_DENSITY, MAX_DENSITY)),
    warmth: tidy(clamp(r.warmth, -1, 1)),
    light: tidy(clamp(r.light, -1, 1)),
    extras: extras.slice(0, MAX_EXTRAS),
    title,
  };
}

/**
 * Recipe.FromJson for untrusted input (the model, KV, the app's "current"). Never throws.
 * An unknown biome word is matched against the kit's words, then each hint in turn (the title,
 * what the player said, the current world); failing all of those it is the first world.
 * Missing fields take the chosen world's defaults; every value ends inside its range.
 */
export function clampRecipe(o, hints = []) {
  const src = o && typeof o === "object" && !Array.isArray(o) ? o : {};
  const word = typeof src.biome === "string" ? src.biome.trim().toLowerCase() : "";
  let id = biome(word) ? word : bestBiome(word);
  for (const h of [src.title, ...hints]) {
    if (id) break;
    if (typeof h !== "string" || h.length === 0) continue;
    id = biome(h) ? h : bestBiome(h);
  }
  const base = recipeFor(id || BIOMES[0].id);
  let extras = base.extras;
  if (Object.prototype.hasOwnProperty.call(src, "extras")) {
    extras = [];
    if (Array.isArray(src.extras)) {
      for (const e of src.extras) {
        if (extras.length >= 8) break;
        if (typeof e === "string") extras.push(e.trim().toLowerCase().replaceAll(" ", "_"));
      }
    }
  }
  return clampFields({
    biome: base.biome,
    hour: num(src.hour, base.hour),
    precip: typeof src.precip === "string" ? src.precip.toLowerCase() : base.precip,
    fog: num(src.fog, base.fog),
    wind: num(src.wind, base.wind),
    density: num(src.density, base.density),
    warmth: num(src.warmth, base.warmth),
    light: num(src.light, base.light),
    extras,
    title: typeof src.title === "string" ? src.title : base.title,
  });
}


// ---------- composed places (mirrors Look.cs; the app checks every field again) ----------

export const HORIZONS = ["mountains", "dunes", "skyline", "hills", "reef", "planet", "sea"];
// The one thing on the horizon that makes a world a real place (Atlas.Landmarks in the app), or "none".
export const LANDMARKS = ["none", "pyramids", "tower", "dome", "pagoda", "clock_tower", "windmill", "arch", "colosseum", "columns", "peak", "stone_circle", "lighthouse", "spire", "onion_domes", "mosque", "step_pyramid", "wall"];
export const SKY_FROM = [...BIOMES.map((b) => b.id), "none"];
export const AMBIENCES = ["amb_forest_night", "amb_beach", "amb_desert", "amb_snow", "amb_space", "amb_reef", "amb_city_jazz", "amb_meadow"];
export const SCATTER_KINDS = [
  // code-made
  "pine", "snow_pine", "oak", "palm", "bush", "fern", "cactus", "dune_grass", "kelp", "coral", "flower", "flower_patch", "rock",
  "snow_rock", "reef_rock", "shell_rock", "stone", "ice", "crystal_cluster", "log_small", "building", "street_lamp", "hydrant",
  "antenna", "panel", "tank", "light_pylon", "lantern", "cloud",
  // artist-made (Kenney, CC0)
  "mushroom", "stump", "tent", "log_stack", "lantern_hanging", "rover", "crate_space", "console", "bar_stool", "lounge_chair",
  "speaker", "potted_plant", "fall_tree", "crater", "crystal_rock", "meteor_rock", "pumpkin", "corn", "fence", "canoe", "lily",
  "parasol", "cone", "traffic_light", "present", "candy_cane", "bookcase", "floor_lamp", "teddy", "radio", "generator", "barrels",
];
export const SKINS = {
  surface: ["bar_table", "cabin_bench", "console", "driftwood_table", "fire_ring", "giant_clam", "picnic_table", "stone_altar"],
  seat: ["booth", "coral_rock", "log_bench", "lounger", "mossy_log", "pilot_seat", "sandstone_block", "snowy_log"],
  tall: ["brick_pillar", "kelp_column", "machine_tower", "oak_trunk", "palm_trunk", "pine_trunk", "rock_pillar", "snow_pine_trunk"],
  light: ["brazier", "jelly_lamp", "lantern_post", "light_pylon", "oil_lamp", "street_lamp", "tiki_torch"],
  plant: ["anemone", "cactus_small", "fern", "flower_bush", "hydroponic", "palm_bush", "potted_palm", "snow_bush"],
};
export const PALETTE_KEYS = ["top", "horizon", "fog", "ground", "ground_accent", "prop", "prop_accent", "emissive", "ambient"];
const HEX = /^#[0-9a-fA-F]{6}$/;

/** A composed look, cleaned: only kit words, real colours, numbers in range. Null if there is none. */
export function cleanLook(v) {
  if (!v || typeof v !== "object" || Array.isArray(v)) return null;
  const str = (x, max) => (typeof x === "string" ? x.trim().slice(0, max) : "");
  const pick = (x, list, fallback) => (typeof x === "string" && list.includes(x.trim().toLowerCase()) ? x.trim().toLowerCase() : fallback);
  const palette = {};
  const pv = v.palette && typeof v.palette === "object" ? v.palette : {};
  for (const k of PALETTE_KEYS) palette[k] = typeof pv[k] === "string" && HEX.test(pv[k].trim()) ? pv[k].trim().toUpperCase() : "";
  const scatter = [];
  for (const k of Array.isArray(v.scatter) ? v.scatter : []) {
    const id = typeof k === "string" ? k.trim().toLowerCase().replaceAll(" ", "_") : "";
    if (SCATTER_KINDS.includes(id) && scatter.length < 8) scatter.push(id);
  }
  const skins = {};
  const sv = v.skins && typeof v.skins === "object" ? v.skins : {};
  for (const role of Object.keys(SKINS)) if (typeof sv[role] === "string" && SKINS[role].includes(sv[role])) skins[role] = sv[role];
  const terrain = Number(v.terrain);
  const tint = Number(v.tint);
  return {
    name: str(v.name, MAX_TITLE), short: str(v.short, 14),
    horizon: pick(v.horizon, HORIZONS, ""), terrain: Number.isFinite(terrain) ? Math.max(0, Math.min(30, terrain)) : 6,
    sky: pick(v.sky, SKY_FROM, ""), ambience: pick(v.ambience, AMBIENCES, ""), tint: Number.isFinite(tint) ? Math.max(0, Math.min(1, tint)) : 0,
    landmark: pick(v.landmark, LANDMARKS, "none"),
    scatter, skins, palette,
  };
}

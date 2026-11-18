import { test } from "node:test";
import assert from "node:assert/strict";
import { cleanLook, SCATTER_KINDS } from "../kit.js";
import { finish, OUTPUT_SCHEMA } from "../worker.js";

test("a composed look keeps only kit words, real colours and numbers in range", () => {
  const l = cleanLook({
    name: "Sunken temple", short: "Temple", horizon: "VOLCANO", terrain: 900, sky: "nowhere", ambience: "amb_desert", tint: -3,
    scatter: ["rock", "dragon", "crystal_rock", 42, "<script>"],
    skins: { surface: "stone_altar", seat: "throne", tall: null },
    palette: { top: "#112233", ground: "red", emissive: "#ffaa00" },
  });
  assert.deepEqual(l.scatter, ["rock", "crystal_rock"]);
  assert.equal(l.horizon, "");
  assert.equal(l.terrain, 30);
  assert.equal(l.sky, "");
  assert.equal(l.tint, 0);
  assert.deepEqual(l.skins, { surface: "stone_altar" });
  assert.equal(l.palette.top, "#112233");
  assert.equal(l.palette.ground, "");
  assert.equal(l.palette.emissive, "#FFAA00");
});

test("no look, or not an object, is null", () => {
  assert.equal(cleanLook(null), null);
  assert.equal(cleanLook("volcano"), null);
  assert.equal(cleanLook([1, 2]), null);
});

test("the model's look reaches the app inside the recipe, with a safe name", () => {
  const a = finish({
    safety: "ok", biome: "desert", hour: 21, precip: "none", fog: 0.5, wind: 0.3, density: 1, warmth: 0, light: 0, extras: [], title: "Lava lake",
    look: { name: "Lava lake", short: "Lava", horizon: "mountains", terrain: 20, sky: "none", ambience: "amb_desert", tint: 0.5, scatter: ["rock"], skins: {}, palette: {} },
    clarify: null, choices: [],
  }, "a lava lake", null);
  assert.equal(a.recipe.look.name, "Lava lake");
  assert.equal(a.recipe.look.horizon, "mountains");
});

test("the schema lists every prop the app can scatter", () => {
  const look = OUTPUT_SCHEMA.properties.look.anyOf.find((x) => x.type === "object");
  assert.deepEqual(look.properties.scatter.items.enum, SCATTER_KINDS);
});

test("seed answers for the app's places carry their composed look", async () => {
  const { SEED_LOOKS } = await import("../worker.js");
  const { PLACE_LOOKS } = await import("../places.js");
  for (const id of Object.values(SEED_LOOKS)) assert.ok(PLACE_LOOKS[id], id);
});

test("every seed with a look is a real seed phrase", async () => {
  const { SEED_LOOKS } = await import("../worker.js");
  const { SEED_MAP, seedKey } = await import("../seeds.js");
  for (const phrase of Object.keys(SEED_LOOKS)) assert.ok(SEED_MAP.get(seedKey(phrase)), phrase);
});

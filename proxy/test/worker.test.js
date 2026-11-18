// Run with: node --test   (from the proxy folder). No network: fetch is mocked in every test.

import { test, describe, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import worker, * as W from "../worker.js";
import * as K from "../kit.js";
import { SEEDS, SEED_MAP, CALM_RECIPE, seedKey } from "../seeds.js";

const KEY = "test-app-key-123";
const ENV = Object.freeze({ APP_KEY: KEY, ANTHROPIC_API_KEY: "sk-ant-test" });
const BASE = "https://worldeck-proxy.test";
const RECIPE_KEYS = ["biome", "hour", "precip", "fog", "wind", "density", "warmth", "light", "extras", "title"];

const realFetch = globalThis.fetch;
const realLog = console.log;
let calls;
let logs;

beforeEach(() => {
  W.resetState();
  calls = [];
  logs = [];
  console.log = (...a) => logs.push(a.join(" "));
  globalThis.fetch = async () => { throw new Error("unexpected network call"); };
});
afterEach(() => {
  globalThis.fetch = realFetch;
  console.log = realLog;
});

function mockFetch(handler) {
  globalThis.fetch = async (url, init = {}) => {
    calls.push({ url: String(url), init });
    return handler(String(url), init);
  };
}

function modelResponse(output, { stop_reason = "end_turn", status = 200 } = {}) {
  const content = output === undefined ? [] : [{ type: "text", text: typeof output === "string" ? output : JSON.stringify(output) }];
  return new Response(JSON.stringify({
    id: "msg_test", type: "message", role: "assistant", model: "claude-haiku-5-5", content, stop_reason,
    usage: { input_tokens: 40, cache_read_input_tokens: 3000, output_tokens: 90 },
  }), { status, headers: { "content-type": "application/json" } });
}

const GOOD = {
  safety: "ok", biome: "beach", hour: 20.5, precip: "none", fog: 0.4, wind: 0.6, density: 0.5, warmth: -0.2,
  light: -0.1, extras: ["lanterns", "clouds"], title: "Glass lighthouse on the coast", clarify: null, choices: [],
};

let devN = 0;
const freshDevice = () => "device-" + String(devN++).padStart(8, "0");

function recipeReq(text, { key = KEY, device = freshDevice(), current = null, kit = 1, body, headers = {} } = {}) {
  const h = { "Content-Type": "application/json", ...headers };
  if (key !== null) h["X-App-Key"] = key;
  if (device !== null) h["X-Device"] = device;
  return new Request(BASE + "/v1/recipe", { method: "POST", headers: h, body: body ?? JSON.stringify({ text, current, kit }) });
}

async function call(request, env = ENV) {
  const pending = [];
  const res = await worker.fetch(request, env, { waitUntil: (p) => pending.push(p) });
  await Promise.all(pending);
  return { res, body: await res.json() };
}

function assertContract(body) {
  assert.deepEqual(Object.keys(body).sort(), ["choices", "clarify", "ms", "recipe", "safety", "source"]);
  assert.ok(["ok", "decline"].includes(body.safety));
  assert.ok(["model", "cache", "seed"].includes(body.source));
  assert.ok(Number.isInteger(body.ms) && body.ms >= 0);
  const r = body.recipe;
  // A composed look is optional and comes last; the app checks it again.
  assert.deepEqual(Object.keys(r).filter((k) => k !== "look"), RECIPE_KEYS);
  if ("look" in r) assert.ok(r.look && typeof r.look.name === "string" && Array.isArray(r.look.scatter));
  assert.ok(K.BIOME_IDS.includes(r.biome));
  assert.ok(r.hour >= 0 && r.hour <= 24);
  assert.ok(K.PRECIP.includes(r.precip));
  for (const k of ["fog", "wind"]) assert.ok(r[k] >= 0 && r[k] <= 1, k);
  assert.ok(r.density >= 0.3 && r.density <= 1.6);
  for (const k of ["warmth", "light"]) assert.ok(r[k] >= -1 && r[k] <= 1, k);
  assert.ok(Array.isArray(r.extras) && r.extras.length <= 4 && r.extras.every((e) => K.EXTRAS.includes(e)));
  assert.ok(typeof r.title === "string" && r.title.length > 0 && r.title.length <= 40);
  assert.ok(body.clarify === null || (typeof body.clarify === "string" && body.clarify.length <= 90));
  assert.ok(Array.isArray(body.choices) && body.choices.length <= 3 && body.choices.every((c) => K.BIOME_IDS.includes(c)));
}

// ---------------------------------------------------------------------------

describe("health and routing", () => {
  test("GET /health", async () => {
    const { res, body } = await call(new Request(BASE + "/health"));
    assert.equal(res.status, 200);
    assert.deepEqual(body, { ok: true, kit: 1 }, "to anyone: alive, and which kit, nothing more");
    assert.match(res.headers.get("X-Request-Id"), /^[0-9a-f]{16}$/);
  });

  test("GET /health with the app key says what is bound, never a value", async () => {
    const env = { ...ENV, DEVICE_LIMITER: { limit: async () => ({ success: true }) } };
    const { body } = await call(new Request(BASE + "/health", { headers: { "X-App-Key": KEY } }), env);
    assert.equal(body.ok, true);
    assert.equal(body.model_key, true);
    assert.equal(body.device_limiter, true);
    assert.equal(body.global_limiter, false);
    assert.equal(body.kv, false);
    assert.equal(body.ai_off, false);
    assert.match(body.pv, /^[0-9a-f]{8}$/);
    const text = JSON.stringify(body);
    for (const secret of [KEY, "sk-ant-test"]) assert.ok(!text.includes(secret), secret);
    // A wrong key gets the public answer.
    const pub = await call(new Request(BASE + "/health", { headers: { "X-App-Key": "nope" } }));
    assert.deepEqual(pub.body, { ok: true, kit: 1 });
  });

  test("AI_OFF: no model call; seeds still answer; the rest is 503 with Retry-After", async () => {
    mockFetch(() => { throw new Error("the model must not be called"); });
    const env = { ...ENV, AI_OFF: "1" };
    const off = await call(recipeReq("a glass lighthouse on a cliff"), env);
    assert.equal(off.res.status, 503);
    assert.equal(off.res.headers.get("Retry-After"), "600");
    assert.equal(JSON.parse(logs[0]).err, "ai_off");
    const seeded = await call(recipeReq("mars"), env);
    assert.equal(seeded.res.status, 200);
    assert.equal(seeded.body.source, "seed");
    assert.equal(calls.length, 0);
  });

  test("unknown path is 404, wrong method is 405", async () => {
    assert.equal((await call(new Request(BASE + "/nope"))).res.status, 404);
    assert.equal((await call(new Request(BASE + "/v1/recipe"))).res.status, 405);
  });
});

describe("auth and request checks", () => {
  test("wrong key is 401 and nothing is called", async () => {
    let r = await call(recipeReq("a castle", { key: "wrong-key" }));
    assert.equal(r.res.status, 401);
    assert.equal(typeof r.body.error, "string");
    r = await call(recipeReq("a castle", { key: null }));
    assert.equal(r.res.status, 401);
    assert.equal(calls.length, 0);
  });

  test("no APP_KEY configured fails closed with 500", async () => {
    const { res } = await call(recipeReq("a castle"), { ...ENV, APP_KEY: undefined });
    assert.equal(res.status, 500);
  });

  test("bad bodies are 400", async () => {
    const cases = [
      recipeReq("", { body: "{not json" }),
      recipeReq("x".repeat(301)),
      recipeReq("   "),
      recipeReq("a castle", { kit: 2 }),
      recipeReq("a castle", { body: JSON.stringify({ text: 42, current: null, kit: 1 }) }),
      recipeReq("a castle", { body: JSON.stringify([1, 2]) }),
      recipeReq("a castle", { current: [1, 2, 3] }),
      recipeReq("a castle", { headers: { "Content-Type": "text/plain" } }),
      recipeReq("a castle", { device: null }),
      recipeReq("a castle", { device: "short" }),
    ];
    for (const req of cases) {
      const { res, body } = await call(req);
      assert.equal(res.status, 400);
      assert.deepEqual(Object.keys(body), ["error"]);
    }
    assert.equal(calls.length, 0);
  });

  test("exactly 300 characters is accepted", async () => {
    mockFetch(() => modelResponse(GOOD));
    const { res } = await call(recipeReq("a lighthouse " + "x".repeat(287)));
    assert.equal(res.status, 200);
  });
});

describe("seeds", () => {
  test("seed hit answers with no model call", async () => {
    const { res, body } = await call(recipeReq("Computer, take me to a Haunted House, please!"));
    assert.equal(res.status, 200);
    assertContract(body);
    assert.equal(body.source, "seed");
    assert.equal(body.safety, "ok");
    const { look, title, ...rest } = body.recipe;
    const { title: seedTitle, ...seedRest } = SEED_MAP.get("haunted house");
    assert.deepEqual(rest, seedRest, "the seed's knobs");
    assert.equal(look.name, "Haunted forest", "and the app's composed look for it");
    assert.equal(title, look.name);
    assert.equal(calls.length, 0);
  });

  test("seeds work with no API key at all", async () => {
    const { res, body } = await call(recipeReq("hogwarts"), { APP_KEY: KEY });
    assert.equal(res.status, 200);
    assert.equal(body.recipe.title, "Castle-town in the snow at night");
    assert.equal(body.recipe.biome, "snow");
  });

  test("40+ seeds, all already inside the kit's ranges, all titles fit to show", () => {
    assert.ok(SEEDS.length >= 40, `only ${SEEDS.length} seeds`);
    for (const s of SEEDS) {
      assert.deepEqual(K.clampRecipe(s.r), s.r, `seed "${s.say[0]}" changes under clamp`);
      assert.ok(W.textOk(s.r.title), `seed title "${s.r.title}"`);
      assert.ok(s.say.every((p) => SEED_MAP.get(seedKey(p)) === s.r), `seed "${s.say[0]}" phrase claimed by another`);
    }
    assert.deepEqual(K.clampRecipe(CALM_RECIPE), { ...CALM_RECIPE });
  });

  test("seedKey strips openers and articles", () => {
    assert.equal(seedKey("Hey computer, let's go to the Northern Lights now"), "northern lights");
    assert.equal(seedKey("I want to go to an aquarium please"), "aquarium");
    assert.equal(seedKey("Take me to Santa’s workshop"), "santa's workshop");
  });
});

describe("model path", () => {
  test("request follows the Messages API shape for Haiku 5.5", async () => {
    mockFetch(() => modelResponse(GOOD));
    const current = { biome: "campfire", hour: 22, precip: "none", fog: 0, wind: 0.3, density: 1, warmth: 0, light: 0, extras: ["fireflies"], title: "Campfire under the stars" };
    const { res, body } = await call(recipeReq("a lighthouse made of glass", { current }));
    assert.equal(res.status, 200);
    assertContract(body);
    assert.equal(body.source, "model");
    assert.equal(calls.length, 1);
    const { url, init } = calls[0];
    assert.equal(url, "https://api.anthropic.com/v1/messages");
    assert.equal(init.method, "POST");
    assert.equal(init.headers["x-api-key"], "sk-ant-test");
    assert.equal(init.headers["anthropic-version"], "2026-10-21");
    assert.equal(init.headers["content-type"], "application/json");
    assert.ok(init.signal, "has an abort signal");
    const sent = JSON.parse(init.body);
    assert.equal(sent.model, "claude-haiku-5-5");
    assert.deepEqual(sent.thinking, { type: "disabled" });
    assert.equal(sent.output_config.effort, "low");
    assert.equal(sent.output_config.format.type, "json_schema");
    assert.deepEqual(sent.output_config.format.schema, W.OUTPUT_SCHEMA);
    for (const banned of ["temperature", "top_p", "top_k", "output_format", "fallbacks"]) assert.ok(!(banned in sent), banned);
    assert.deepEqual(sent.system, [{ type: "text", text: W.SYSTEM_PROMPT, cache_control: { type: "ephemeral" } }]);
    assert.equal(sent.messages.length, 1);
    assert.equal(sent.messages.at(-1).role, "user", "no assistant prefill");
    assert.match(sent.messages[0].content, /<said>a lighthouse made of glass<\/said>/);
    assert.match(sent.messages[0].content, /^Current world: \{"biome":"campfire"/);
  });

  test("hostile model output is clamped into the kit", async () => {
    mockFetch(() => modelResponse({
      safety: "OK", biome: "snowy mountain peaks", hour: 99, precip: "lava", fog: 5, wind: -3, density: 50,
      warmth: "hot", light: -9,
      extras: ["aurora", "lasers", "aurora", "Shooting Stars", "embers", "fireflies", "clouds", 7],
      title: "<b>Peak</b> of the world!!! ~~~ a very long title that keeps going and going",
      clarify: "Which one?" + " really".repeat(40),
      choices: ["space", "mordor", "space", "reef", "city", "beach"],
      url: "http://evil.example", html: "<script>alert(1)</script>",
    }));
    const { res, body } = await call(recipeReq("the top of the world"));
    assert.equal(res.status, 200);
    assertContract(body);
    const r = body.recipe;
    assert.equal(body.safety, "ok");
    assert.equal(r.biome, "snow", "unknown biome → nearest by the kit's words");
    assert.equal(r.hour, 3);
    assert.equal(r.precip, "none");
    assert.equal(r.fog, 1);
    assert.equal(r.wind, 0);
    assert.equal(r.density, 1.6);
    assert.equal(r.warmth, 0, "a non-number takes the default");
    assert.equal(r.light, -1);
    assert.deepEqual(r.extras, ["aurora", "shooting_stars", "embers", "fireflies"]);
    assert.ok(r.title.length <= 40 && !/[<>~]/.test(r.title), r.title);
    assert.ok(body.clarify.length <= 90 && body.clarify.endsWith("?"));
    assert.deepEqual(body.choices, ["space", "reef", "city"]);
    assert.ok(!JSON.stringify(body).includes("evil") && !JSON.stringify(body).includes("script"));
  });

  test("hostile output: reef and space rules, missing fields, hints", async () => {
    mockFetch(() => modelResponse({ safety: "ok", biome: "underwater kingdom", precip: "rain", extras: ["aurora", "lanterns"], title: "" }));
    let { body } = await call(recipeReq("a kingdom below the waves"));
    assert.equal(body.recipe.biome, "reef");
    assert.equal(body.recipe.precip, "bubbles");
    assert.deepEqual(body.recipe.extras, ["lanterns"]);
    assert.equal(body.recipe.title, "Coral reef underwater", "blank title → the world's own name");

    mockFetch(() => modelResponse({ safety: "ok", biome: "orbit-ish", precip: "snow", extras: ["clouds", "crystals"], title: "Way up high" }));
    ({ body } = await call(recipeReq("way up high")));
    assert.equal(body.recipe.biome, "space");
    assert.equal(body.recipe.precip, "none");
    assert.deepEqual(body.recipe.extras, ["crystals"]);

    mockFetch(() => modelResponse({ safety: "ok", title: "Somewhere" }));
    ({ body } = await call(recipeReq("a sea cave with seals")));
    assert.equal(body.recipe.biome, "beach", "no biome → the player's words decide");
    assert.equal(body.recipe.hour, 18.5, "missing fields take the world's defaults");
  });

  test("a non-object answer, an HTTP error, a cut-off answer or no key is 502", async () => {
    for (const reply of [
      () => modelResponse("[1,2,3]"),
      () => modelResponse("not json at all"),
      () => modelResponse(undefined),
      () => modelResponse(GOOD, { stop_reason: "max_tokens" }),
      () => new Response(JSON.stringify({ type: "error", error: { type: "overloaded_error" } }), { status: 529 }),
      () => { throw new TypeError("network down"); },
    ]) {
      mockFetch(reply);
      const { res, body } = await call(recipeReq("a lighthouse made of glass"));
      assert.equal(res.status, 502);
      assert.deepEqual(Object.keys(body), ["error"]);
    }
    const { res } = await call(recipeReq("a lighthouse made of glass"), { APP_KEY: KEY });
    assert.equal(res.status, 502);
  });

  test("timeout aborts the call and returns 504", async () => {
    assert.equal(W.MODEL_TIMEOUT_MS, 9000);
    let aborted = false;
    mockFetch((url, init) => new Promise((resolve, reject) => {
      init.signal.addEventListener("abort", () => { aborted = true; reject(new DOMException("aborted", "AbortError")); });
    }));
    const t0 = Date.now();
    const { res, body } = await call(recipeReq("a lighthouse made of glass"), { ...ENV, MODEL_TIMEOUT_MS: "40" });
    assert.equal(res.status, 504);
    assert.equal(typeof body.error, "string");
    assert.ok(aborted, "the fetch was aborted");
    assert.ok(Date.now() - t0 < 2000);
  });
});

describe("cache", () => {
  test("second ask is a cache hit, matched after normalising", async () => {
    mockFetch(() => modelResponse(GOOD));
    const first = await call(recipeReq("a lighthouse made of glass"));
    const second = await call(recipeReq("  A LIGHTHOUSE made of glass!  "));
    assert.equal(first.body.source, "model");
    assert.equal(second.body.source, "cache");
    assert.deepEqual(second.body.recipe, first.body.recipe);
    assertContract(second.body);
    assert.equal(calls.length, 1);
  });

  test("KV is used when bound, keyed by a hash, and re-validated on read", async () => {
    const kv = new Map();
    const RECIPES = {
      get: async (k, type) => (kv.has(k) ? (type === "json" ? JSON.parse(kv.get(k)) : kv.get(k)) : null),
      put: async (k, v) => { kv.set(k, v); },
    };
    const env = { ...ENV, RECIPES };
    mockFetch(() => modelResponse(GOOD));
    await call(recipeReq("a lighthouse made of glass"), env);
    assert.equal(kv.size, 1);
    const [k, v] = [...kv][0];
    assert.match(k, /^r:[0-9a-f]{64}$/);
    assert.ok(!v.includes("made of glass"), "no transcript in KV");

    W.resetState(); // a new isolate: LRU empty, KV still there
    const again = await call(recipeReq("a lighthouse made of glass"), env);
    assert.equal(again.body.source, "cache");
    assert.equal(calls.length, 1);

    // Tampered KV data is clamped like model output.
    kv.set(k, JSON.stringify({ safety: "ok", recipe: { biome: "city", density: 99, title: "porn palace" }, clarify: null, choices: [] }));
    W.resetState();
    const tampered = await call(recipeReq("a lighthouse made of glass"), env);
    assertContract(tampered.body);
    assert.equal(tampered.body.recipe.density, 1.6);
    assert.equal(tampered.body.recipe.title, "Jazz club at 2 a.m.");
  });

  test("a KV error never breaks a request", async () => {
    const env = { ...ENV, RECIPES: { get: async () => { throw new Error("kv down"); }, put: async () => { throw new Error("kv down"); } } };
    mockFetch(() => modelResponse(GOOD));
    const { res, body } = await call(recipeReq("a lighthouse made of glass"), env);
    assert.equal(res.status, 200);
    assert.equal(body.source, "model");
  });
});

describe("safety", () => {
  test("model decline returns the calm world, whatever else it said", async () => {
    mockFetch(() => modelResponse({
      safety: "decline", biome: "city", hour: 3, precip: "rain", fog: 1, wind: 1, density: 1.6, warmth: -1, light: -1,
      extras: ["embers"], title: "Something awful", clarify: "Are you sure?", choices: ["city"],
    }));
    const { res, body } = await call(recipeReq("something the model would not build"));
    assert.equal(res.status, 200);
    assertContract(body);
    assert.equal(body.safety, "decline");
    assert.deepEqual(body.recipe, { ...CALM_RECIPE });
    assert.equal(body.clarify, null);
    assert.deepEqual(body.choices, []);
    assert.equal(body.source, "model");
  });

  test("an unknown safety value declines", async () => {
    mockFetch(() => modelResponse({ ...GOOD, safety: "maybe" }));
    const { body } = await call(recipeReq("a lighthouse made of glass"));
    assert.equal(body.safety, "decline");
  });

  test("a classifier refusal (stop_reason refusal) declines and is not cached", async () => {
    mockFetch(() => modelResponse(undefined, { stop_reason: "refusal" }));
    const first = await call(recipeReq("a lighthouse made of glass"));
    assert.equal(first.res.status, 200);
    assert.equal(first.body.safety, "decline");
    assert.deepEqual(first.body.recipe, { ...CALM_RECIPE });
    await call(recipeReq("a lighthouse made of glass"));
    assert.equal(calls.length, 2);
  });

  test("plainly off-limits words decline with no model call", async () => {
    const { body } = await call(recipeReq("show me something sexy"));
    assert.equal(body.safety, "decline");
    assert.equal(body.source, "seed");
    assert.equal(calls.length, 0);
    assert.equal(W.hardBlocked("a grape vineyard in essex"), false, "whole words only");
  });

  test("title profanity, brands and people are blanked to the world's name", async () => {
    const titles = [
      ["Shit-hot lighthouse", "a"], ["s h i t coast", "b"], ["Bloody cove", "c"], ["Hogwarts by the sea", "d"],
      ["Taylor Swift's beach", "e"], ["Visit www.example.com", "f"],
    ];
    for (const [title, tag] of titles) {
      mockFetch(() => modelResponse({ ...GOOD, title }));
      const { body } = await call(recipeReq("a lighthouse made of glass " + tag));
      assert.equal(body.recipe.title, "Tropical beach at sunset", title);
    }
  });

  test("clarify is checked too, and passes through when clean", async () => {
    mockFetch(() => modelResponse({ ...GOOD, clarify: "What the fuck?", choices: ["beach", "reef"] }));
    let { body } = await call(recipeReq("the pool 1"));
    assert.equal(body.clarify, null);
    assert.deepEqual(body.choices, []);

    mockFetch(() => modelResponse({ ...GOOD, clarify: "A sunny poolside, or under the water?", choices: ["beach", "reef", "beach"] }));
    ({ body } = await call(recipeReq("the pool 2")));
    assert.equal(body.clarify, "A sunny poolside, or under the water?");
    assert.deepEqual(body.choices, ["beach", "reef"]);
  });
});

describe("rate limits", () => {
  test("12 per device per minute, then 429 with Retry-After", async () => {
    for (let i = 0; i < 12; i++) assert.equal((await call(recipeReq("a castle", { device: "rate-device-one" }))).res.status, 200);
    const { res, body } = await call(recipeReq("a castle", { device: "rate-device-one" }));
    assert.equal(res.status, 429);
    assert.deepEqual(Object.keys(body), ["error"]);
    assert.ok(Number(res.headers.get("Retry-After")) >= 1);
    assert.equal((await call(recipeReq("a castle", { device: "rate-device-two" }))).res.status, 200);
  });

  test("300 per minute across all devices", async () => {
    for (let d = 0; d < 30; d++) {
      for (let i = 0; i < 10; i++) assert.equal((await call(recipeReq("a castle", { device: `global-device-${d}` }))).res.status, 200);
    }
    assert.equal((await call(recipeReq("a castle", { device: "global-device-new" }))).res.status, 429);
  });

  test("the Cloudflare Rate Limiting binding is used when bound, and its failure is ignored", async () => {
    const deny = { limit: async () => ({ success: false }) };
    assert.equal((await call(recipeReq("a castle"), { ...ENV, DEVICE_LIMITER: deny })).res.status, 429);
    const broken = { limit: async () => { throw new Error("binding down"); } };
    assert.equal((await call(recipeReq("a castle"), { ...ENV, GLOBAL_LIMITER: broken })).res.status, 200);
  });
});

describe("speech", () => {
  test("speech is never sent to the relay: /v1/stt does not exist", async () => {
    const res = await worker.fetch(new Request(BASE + "/v1/stt", { method: "POST", headers: { "X-App-Key": KEY, "Content-Type": "audio/raw" }, body: new Uint8Array(3200) }), ENV);
    assert.equal(res.status, 404);
  });
});

describe("logs", () => {
  test("one line per request, with no text, key or raw device id", async () => {
    mockFetch(() => modelResponse(GOOD));
    await call(recipeReq("my secret zebra place", { device: "privacy-device-0001" }));
    assert.equal(logs.length, 1);
    const line = JSON.parse(logs[0]);
    assert.deepEqual(Object.keys(line).sort(), ["ep", "model_ms", "ms", "pv", "rid", "source", "status", "stop", "tok"]);
    assert.equal(line.ep, "/v1/recipe");
    assert.equal(line.status, 200);
    assert.equal(line.source, "model");
    assert.match(line.rid, /^[0-9a-f]{16}$/);
    assert.deepEqual(line.tok, [40, 90, 3000, 0], "input, output, cache read, cache write");
    assert.equal(line.stop, "end_turn");
    assert.ok(Number.isInteger(line.model_ms) && line.model_ms >= 0);
    assert.ok(!("dev" in line), "no device id, and no hash of it");
    for (const secret of ["zebra", "privacy-device-0001", KEY, "sk-ant-test"]) assert.ok(!logs[0].includes(secret), secret);

    await call(new Request(BASE + "/some/user/text/in/the/path"));
    assert.equal(JSON.parse(logs[1]).ep, "other");
  });

  test("the request id in the log is the one the app gets back", async () => {
    mockFetch(() => modelResponse(GOOD));
    const { res } = await call(recipeReq("a glass lighthouse on a cliff"));
    assert.equal(res.headers.get("X-Request-Id"), JSON.parse(logs[0]).rid);
  });

  test("refusals, declines, limits and bad requests say why, in codes", async () => {
    await call(recipeReq("hello", { key: "wrong" }));
    assert.equal(JSON.parse(logs[0]).why, "app_key");
    await call(recipeReq("hello", { kit: 99 }));
    assert.equal(JSON.parse(logs[1]).why, "kit_must_be_");
    assert.ok(!logs[1].includes("hello"));
    const dev = freshDevice();
    mockFetch(() => modelResponse(GOOD));
    for (let i = 0; i < W.DEVICE_PER_MIN + 1; i++) await call(recipeReq("a glass lighthouse number " + i, { device: dev }));
    assert.equal(JSON.parse(logs[logs.length - 1]).why, "limit_device");
  });

  test("errors carry an error class, still no text", async () => {
    mockFetch(() => new Response("{}", { status: 500 }));
    await call(recipeReq("my secret zebra place"));
    const line = JSON.parse(logs[0]);
    assert.equal(line.status, 502);
    assert.equal(line.err, "model_http_500");
    assert.ok(!logs[0].includes("zebra"));
  });
});

describe("prompt, schema and kit", () => {
  test("prompt.md holds exactly the embedded system prompt", () => {
    const md = readFileSync(new URL("../prompt.md", import.meta.url), "utf8").replace(/\r\n/g, "\n");
    const m = md.match(/<!-- prompt:start -->\n````text\n([\s\S]*?)\n````\n<!-- prompt:end -->/);
    assert.ok(m, "prompt.md has the prompt:start/end block");
    assert.equal(m[1], W.SYSTEM_PROMPT, "prompt.md is out of date: run node scripts/write-prompt-md.js");
  });

  test("the prompt names the whole vocabulary and has 20+ worked examples", () => {
    for (const id of K.BIOME_IDS) assert.ok(W.SYSTEM_PROMPT.includes(`- ${id}, "`), id);
    for (const e of K.EXTRAS) assert.ok(W.SYSTEM_PROMPT.includes(`  - ${e}:`), e);
    for (const p of K.PRECIP) assert.ok(W.SYSTEM_PROMPT.includes(`"${p}"`), p);
    const examples = W.SYSTEM_PROMPT.split("<said>").length - 1;
    assert.ok(examples >= 20, `${examples} examples`);
    for (const p of ["castle", "mars", "tokyo", "haunted house", "rainforest", "aquarium", "northern lights", "cozy library",
      "volcano", "moon base", "christmas", "halloween", "dinosaurs", "ski lodge", "zen garden", "savanna", "dragon's lair",
      "enchanted forest", "above the clouds", "cave"]) assert.ok(W.SYSTEM_PROMPT.includes(`${p}</said>`), p);
    assert.ok(W.SYSTEM_PROMPT.length > 4000, "long enough to be cached (512-token minimum)");
  });

  test("output schema stays inside the structured-outputs subset", () => {
    const walk = (s) => {
      for (const k of ["minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "multipleOf", "pattern"]) assert.ok(!(k in s), k);
      if (s.type === "object") {
        assert.equal(s.additionalProperties, false);
        assert.deepEqual([...s.required].sort(), Object.keys(s.properties).sort());
        Object.values(s.properties).forEach(walk);
      }
      if (s.items) walk(s.items);
      if (s.anyOf) s.anyOf.forEach(walk);
    };
    walk(W.OUTPUT_SCHEMA);
    assert.deepEqual(W.OUTPUT_SCHEMA.properties.biome.enum, K.BIOME_IDS);
    assert.deepEqual(W.OUTPUT_SCHEMA.properties.extras.items.enum, K.EXTRAS);
  });

  const core = (f) => new URL(`../../Assets/Worldeck/Scripts/Core/${f}`, import.meta.url);
  const haveCs = existsSync(core("Kit.cs")) && existsSync(core("Words.cs")) && existsSync(core("Recipe.cs"));

  test("kit.js mirrors Kit.cs, Recipe.cs and Words.cs", { skip: haveCs ? false : "C# sources not next to the proxy" }, () => {
    const kit = readFileSync(core("Kit.cs"), "utf8");
    const words = readFileSync(core("Words.cs"), "utf8");
    const recipe = readFileSync(core("Recipe.cs"), "utf8");
    const strings = (s) => [...s.matchAll(/"([^"]*)"/g)].map((m) => m[1]);
    const snake = (n) => n.replace(/([a-z])([A-Z])/g, "$1_$2").toLowerCase();
    const alls = [...kit.matchAll(/static readonly string\[\] All = \{([^}]*)\}/g)].map((m) => m[1].split(",").map((s) => s.trim()));
    assert.deepEqual(alls[0].map((s) => s.toLowerCase()), K.PRECIP);
    assert.deepEqual(alls[1].map(snake), K.EXTRAS);
    assert.ok(!kit.includes("DefaultFog ="), "a DefaultFog was added in Kit.cs: mirror it in kit.js");
    const parsed = kit.split("new BiomeDef").slice(1).map((b) => {
      const extras = b.match(/DefaultExtras = new\[\] \{([^}]*)\}/);
      return {
        id: b.match(/Id = "([^"]+)"/)[1],
        name: b.match(/Name = "([^"]+)"/)[1],
        short: b.match(/Short = "([^"]+)"/)[1],
        defaultHour: parseFloat(b.match(/DefaultHour = ([\d.]+)f/)[1]),
        hasTime: !/HasTime = false/.test(b),
        defaultPrecip: (b.match(/DefaultPrecip = Precip\.(\w+)/)?.[1] ?? "None").toLowerCase(),
        defaultExtras: extras ? [...extras[1].matchAll(/Extras\.(\w+)/g)].map((x) => snake(x[1])) : [],
        words: strings(b.match(/Words = new\[\] \{([^}]*)\}/)[1]),
      };
    });
    assert.deepEqual(parsed, K.BIOMES.map(({ id, name, short, defaultHour, hasTime, defaultPrecip, defaultExtras, words }) =>
      ({ id, name, short, defaultHour, hasTime, defaultPrecip, defaultExtras, words })));
    assert.equal(Number(kit.match(/const int Version = (\d+)/)[1]), K.KIT_VERSION);
    assert.deepEqual(strings(words.match(/Blocked =\s*\{([^}]*)\}/)[1]), K.BLOCKED);
    assert.deepEqual(new Set(strings(words.match(/WeatherWords = new HashSet<string>\s*\{([^}]*)\}/)[1])), K.WEATHER_WORDS);
    assert.equal(Number(words.match(/MaxTitle = (\d+)/)[1]), K.MAX_TITLE);
    const dens = recipe.match(/MinDensity = ([\d.]+)f, MaxDensity = ([\d.]+)f/);
    assert.deepEqual([Number(dens[1]), Number(dens[2])], [K.MIN_DENSITY, K.MAX_DENSITY]);
    assert.equal(Number(recipe.match(/MaxExtras = (\d+)/)[1]), K.MAX_EXTRAS);
  });

  test("bestBiome and cleanTitle behave like Words.cs", () => {
    assert.equal(K.bestBiome("a snowy forest at night"), "campfire", "weather words count less");
    assert.equal(K.bestBiome("underwater city"), null, "a tie under 1.5 is no answer");
    assert.equal(K.bestBiome("Cabin in the woods"), "campfire");
    assert.equal(K.cleanTitle("Jazz club at 2 a.m."), "Jazz club at 2 a.m.");
    assert.equal(K.cleanTitle("p o r n"), "");
    assert.equal(K.cleanTitle("A".repeat(39) + " -x"), "A".repeat(39));
  });
});

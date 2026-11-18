// Run with: node --test   (from the proxy folder). No network: fetch is mocked in every test.

import { test, describe, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import worker, * as W from "../worker.js";
import * as A from "../act.js";

const KEY = "test-app-key-123";
const ENV = Object.freeze({ APP_KEY: KEY, ANTHROPIC_API_KEY: "sk-ant-test" });
const BASE = "https://worldeck-proxy.test";
const realFetch = globalThis.fetch;
const realLog = console.log;
let calls;

beforeEach(() => {
  W.resetState();
  calls = [];
  console.log = () => {};
  globalThis.fetch = async () => { throw new Error("unexpected network call"); };
});
afterEach(() => {
  globalThis.fetch = realFetch;
  console.log = realLog;
});

const blank = { recipe: null, name: null, kind: null, parts: null, place: null, count: null, size: null, target: null, scale: null, color: null, remove: null, event: null };
const ok = (s) => W.textOk(s);

function modelSays(obj) {
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), body: JSON.parse(init.body) });
    return new Response(JSON.stringify({ stop_reason: "end_turn", content: [{ type: "text", text: JSON.stringify(obj) }] }), { status: 200 });
  };
}

function act(body, device = "11111111-2222-4333-8444-555555555555") {
  return worker.fetch(new Request(BASE + "/v1/act", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-App-Key": KEY, "X-Device": device },
    body: JSON.stringify(body),
  }), ENV);
}

describe("finishAct", () => {
  test("a library thing is kept, clamped and placed", () => {
    const a = A.finishAct({ safety: "ok", say: "A lantern.", actions: [{ ...blank, do: "make", name: "Lantern!", kind: "lantern", place: "table", count: 40, size: 9 }] }, ok);
    assert.equal(a.actions.length, 1);
    assert.deepEqual(a.actions[0], { do: "make", name: "lantern", kind: "lantern", parts: [], place: "table", count: A.MAX_COUNT, size: A.MAX_SIZE });
  });

  test("a built thing keeps only well-formed parts, at most MAX_PARTS", () => {
    const good = "box 0 0.5 0 0.4 0.4 0.4 0 0 0 #AA5522";
    const parts = [good, "pyramid 0 0 0 1 1 1 0 0 0 #ffffff", "box 0 0 0 1 1 #fff", "sphere 9 9 9 9 9 9 0 0 0 #00ff00", 7];
    for (let i = 0; i < 40; i++) parts.push(good);
    const a = A.finishAct({ safety: "ok", say: "Built.", actions: [{ ...blank, do: "make", name: "crate", parts, size: 0.3 }] }, ok);
    const made = a.actions[0];
    assert.equal(made.kind, null);
    assert.equal(made.parts.length, A.MAX_PARTS);
    assert.equal(made.parts[0], "box 0 0.5 0 0.4 0.4 0.4 0 0 0 #aa5522");
    assert.equal(made.parts[1], "sphere 0.75 1.25 0.75 1.5 1.5 1.5 0 0 0 #00ff00", "numbers are clamped into the unit box");
    assert.equal(made.place, "front");
  });

  test("nothing to build means no action", () => {
    const a = A.finishAct({ safety: "ok", say: "Hmm.", actions: [{ ...blank, do: "make", name: "ghost", parts: ["nonsense"] }] }, ok);
    assert.equal(a.actions.length, 0);
    assert.match(a.say, /Hmm|can't/);
  });

  test("changes, events and world edits are checked; unknown actions are dropped", () => {
    const a = A.finishAct({
      safety: "ok", say: "Done.",
      actions: [
        { ...blank, do: "change", target: "last", scale: 100 },
        { ...blank, do: "event", event: "storm" },
        { ...blank, do: "event", event: "format_disk" },
        { ...blank, do: "delete_everything" },
        { ...blank, do: "world", recipe: { biome: "beach", hour: 99, precip: "rain", fog: 5, wind: 0.2, density: 1, warmth: 0, light: 0, extras: ["lanterns", "lasers"], title: "Rainy beach" } },
      ],
    }, ok);
    assert.deepEqual(a.actions.map((x) => x.do), ["change", "event", "world"]);
    assert.equal(a.actions[0].scale, 4);
    assert.equal(a.actions[2].recipe.biome, "beach");
    assert.ok(a.actions[2].recipe.fog <= 1);
    assert.ok(!a.actions[2].recipe.extras.includes("lasers"));
  });

  test("at most MAX_ACTIONS actions; a decline carries none", () => {
    const many = [];
    for (let i = 0; i < 10; i++) many.push({ ...blank, do: "event", event: "music" });
    assert.equal(A.finishAct({ safety: "ok", say: "x", actions: many }, ok).actions.length, A.MAX_ACTIONS);
    const d = A.finishAct({ safety: "decline", say: "no", actions: many }, ok);
    assert.equal(d.safety, "decline");
    assert.equal(d.actions.length, 0);
  });

  test("the schema lists every field as required and allows nothing else", () => {
    const item = A.ACT_SCHEMA.properties.actions.items;
    assert.equal(item.additionalProperties, false);
    assert.deepEqual([...item.required].sort(), Object.keys(item.properties).sort());
  });
});

describe("POST /v1/act", () => {
  test("asks the model with the act prompt and returns clamped actions", async () => {
    modelSays({ safety: "ok", say: "One rubber duck.", actions: [{ ...blank, do: "make", name: "rubber duck", parts: ["sphere 0 0.3 0 0.7 0.55 0.5 0 0 0 #f5c518"], place: "front", count: 1, size: 0.25 }] });
    const res = await act({ text: "make me a rubber duck", kit: 1, current: null, objects: ["Piano!!"], places: ["front", "table", "sofa"] });
    assert.equal(res.status, 200);
    const j = await res.json();
    assert.equal(j.source, "model");
    assert.equal(j.actions[0].name, "rubber duck");
    assert.equal(calls.length, 1);
    assert.equal(calls[0].body.system[0].text, A.ACT_PROMPT);
    assert.match(calls[0].body.messages[0].content, /Already made: piano/);
    assert.match(calls[0].body.messages[0].content, /<said>make me a rubber duck<\/said>/);
  });

  test("the same request again comes from the cache", async () => {
    modelSays({ safety: "ok", say: "Storm.", actions: [{ ...blank, do: "event", event: "storm" }] });
    await act({ text: "start a storm", kit: 1 });
    const res = await act({ text: "start a storm", kit: 1 });
    assert.equal((await res.json()).source, "cache");
    assert.equal(calls.length, 1);
  });

  test("off-limits words are declined without asking the model", async () => {
    const res = await act({ text: "make a torture room", kit: 1 });
    const j = await res.json();
    assert.equal(j.safety, "decline");
    assert.equal(j.actions.length, 0);
    assert.equal(calls.length, 0);
  });

  test("bad bodies and a wrong key are refused", async () => {
    assert.equal((await act({ text: "", kit: 1 })).status, 400);
    assert.equal((await act({ text: "a duck", kit: 2 })).status, 400);
    const res = await worker.fetch(new Request(BASE + "/v1/act", { method: "POST", headers: { "Content-Type": "application/json", "X-App-Key": "nope", "X-Device": "11111111-2222-4333-8444-555555555555" }, body: "{}" }), ENV);
    assert.equal(res.status, 401);
  });
});

describe("asking about the thing you hold", () => {
  test("a clean answer is kept, with no actions needed", () => {
    const a = A.finishAct({ safety: "ok", say: "Good question.", answer: "Scientists think about 20 km an hour.", actions: [] }, ok);
    assert.equal(a.answer, "Scientists think about 20 km an hour.");
    assert.deepEqual(a.actions, []);
  });
  test("a long answer is cut at a sentence, a link is dropped", () => {
    const long = "It was big. ".repeat(30);
    const a = A.finishAct({ safety: "ok", say: "Hm.", answer: long, actions: [] }, ok);
    assert.ok(a.answer.length <= A.MAX_ANSWER && a.answer.endsWith("."));
    assert.equal(A.finishAct({ safety: "ok", say: "Hm.", answer: "See https://example.com", actions: [] }, ok).answer, undefined);
  });
  test("what is held reaches the model", async () => {
    modelSays({ safety: "ok", say: "Good question.", answer: "About 20 km an hour.", actions: [] });
    const res = await act({ text: "how fast could it run?", kit: 1, current: null, objects: ["t rex"], places: ["front"], holding: "t rex" });
    const body = await res.json();
    assert.equal(body.answer, "About 20 km an hour.");
    assert.match(calls[0].body.messages[0].content, /Holding up: t rex/);
  });
});

describe("talking to someone who lives in the world", () => {
  test("who the player is talking to reaches the model, cleaned", async () => {
    modelSays({ safety: "ok", say: "Ha!", answer: "Found it twice, lost it three times.", actions: [] });
    const res = await act({ text: "did you ever find treasure?", kit: 1, current: null, objects: [], places: ["front"], persona: "Captain Brine, a retired pirate captain, at Pirate cove at sunset <b>" });
    const body = await res.json();
    assert.equal(body.answer, "Found it twice, lost it three times.");
    assert.match(calls[0].body.messages[0].content, /Talking to: captain brine, a retired pirate captain, at pirate cove at sunset b\n/);
  });
});

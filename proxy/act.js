// Worldeck proxy: "say anything". The player asks for a thing or a change; Claude answers with a short list of
// actions the app knows how to carry out. Two tools: the app's own library of objects and world settings, and a
// building kit (up to 28 simple coloured shapes) for anything the library does not have. Everything the model
// writes is clamped here and again in the app; the model can never do anything outside this list.

import { BIOME_IDS, PRECIP, EXTRAS, clampRecipe, cleanTitle } from "./kit.js";
import { MADE_KINDS } from "./made-kinds.js";

export const ACT_TIMEOUT_MS = 12000;
export const MAX_ACTIONS = 4;
export const MAX_PARTS = 28;
export const MAX_COUNT = 6;
export const MIN_SIZE = 0.05;
export const MAX_SIZE = 1.5;
export const MAX_SAY = 90;
export const MAX_NAME = 24;
export const MAX_ANSWER = 200;

/** Objects the app can already build (Props.cs and Builds.cs). */
export const KINDS = [
  "pine", "snow_pine", "oak", "palm", "bush", "fern", "cactus", "dune_grass", "kelp", "coral", "flower", "flower_patch",
  "rock", "snow_rock", "reef_rock", "shell_rock", "stone", "ice", "crystal_cluster", "log_small", "kindling",
  "lantern", "street_lamp", "light_pylon", "antenna", "panel", "tank", "hydrant", "building", "cloud",
  "shell", "pearl", "snowball", "seed", "sun_shard", "energy_cell", "record",
  "fire", "sandcastle", "sun_crystal", "snowman", "star_map", "anemone_glow", "jukebox", "blossom_tree",
  // the world's own set dressing (Kenney models)
  "mushroom", "stump", "tent", "log_stack", "lantern_hanging", "rover", "crate_space", "console", "bar_stool",
  "lounge_chair", "speaker", "potted_plant", "pumpkin", "present", "candy_cane", "teddy", "radio", "parasol", "canoe",
  "fence", "barrels", "bookcase", "floor_lamp", "cone", "crystal_rock", "fall_tree", "generator",
  // real models of animals, dinosaurs, people and things (generated from Tools/kit/made.py)
  ...Object.keys(MADE_KINDS),
];
export const PLACES = ["front", "table", "floor", "left", "right", "there", "sofa", "shelf", "bed"];
export const EVENTS = ["storm", "rewind", "music", "music_off", "freeze", "resume", "next_world", "my_room", "quiet", "mute", "sound_on", "louder", "tour"];
export const SHAPES = ["box", "cylinder", "cone", "sphere"];
export const DO = ["world", "make", "change", "event"];

const nullable = (schema) => ({ anyOf: [schema, { type: "null" }] });

const RECIPE_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["biome", "hour", "precip", "fog", "wind", "density", "warmth", "light", "extras", "title"],
  properties: {
    biome: { type: "string", enum: BIOME_IDS },
    hour: { type: "number" },
    precip: { type: "string", enum: PRECIP },
    fog: { type: "number" },
    wind: { type: "number" },
    density: { type: "number" },
    warmth: { type: "number" },
    light: { type: "number" },
    extras: { type: "array", items: { type: "string", enum: EXTRAS } },
    title: { type: "string" },
  },
};

// No minimum/maximum/maxItems in the API's schema subset: counts and ranges are enforced in finishAct.
export const ACT_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["safety", "say", "answer", "actions"],
  properties: {
    safety: { type: "string", enum: ["ok", "decline"] },
    say: { type: "string" },
    answer: nullable({ type: "string" }),
    actions: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["do", "recipe", "name", "kind", "parts", "place", "count", "size", "target", "scale", "color", "remove", "event"],
        properties: {
          do: { type: "string", enum: DO },
          recipe: nullable(RECIPE_SCHEMA),
          name: nullable({ type: "string" }),
          kind: nullable({ type: "string", enum: KINDS }),
          parts: nullable({ type: "array", items: { type: "string" } }),
          place: nullable({ type: "string", enum: PLACES }),
          count: nullable({ type: "number" }),
          size: nullable({ type: "number" }),
          target: nullable({ type: "string" }),
          scale: nullable({ type: "number" }),
          color: nullable({ type: "string" }),
          remove: nullable({ type: "boolean" }),
          event: nullable({ type: "string", enum: EVENTS }),
        },
      },
    },
  },
};

export const ACT_PROMPT = `You are Juniper, the guide in Worldeck, a mixed-reality app on Meta Quest 3. The player sits at their real table; their room has become a world. They say what they want. You answer with JSON only: a short line to say, and up to ${MAX_ACTIONS} actions the app will carry out. You can only do what the actions below allow.

Actions (set every unused field to null):
- do "world": change the world itself. recipe = the complete new world (start from the current one and change only what was asked): biome, hour 0-24, precip, fog 0-1, wind 0-1, density 0.3-1.6, warmth -1..1, light -1..1, extras, title (at most 40 characters, plain words).
- do "make": put a thing in front of the player. name = what it is, at most ${MAX_NAME} characters, lower case ("piano"). place = where: "front" (floating at arm's length, the default), "table", "floor", "left", "right", "there" (where they point), "sofa", "shelf", "bed". count 1-${MAX_COUNT}. size = its largest dimension in metres, ${MIN_SIZE}-${MAX_SIZE} (things on the table are 0.1-0.4; things on the floor up to 1.5).
  - If the thing is in the library, or the library has the same kind of thing (a "puppy" is dog, a "unicorn" is horse, a "dragon" is dinosaur, a "kitten" is cat), set kind to its id and parts to null. These are real models and look far better than shapes: use one whenever it fits. Library: ${KINDS.join(", ")}.
  - Otherwise set kind to null and BUILD it in parts: at most ${MAX_PARTS} strings, each "shape x y z sx sy sz rx ry rz #rrggbb". shape is box, cylinder, cone or sphere. The thing lives in a unit box: x and z from -0.5 to 0.5, y from 0 (its base) to 1 (its top). x y z is the centre of the part; sx sy sz its full size; rx ry rz its rotation in degrees. Cylinders and cones stand along y before rotation. Use few, well-chosen parts with clear silhouettes and 2-4 flat colours; a recognisable toy version is the goal. Make sure parts touch each other and the thing stands on y = 0.
- do "change": alter a thing already made. target = its name, or "last". scale = multiply its size (0.25-4), or color = "#rrggbb" to tint it, or remove = true.
- do "event": one of ${EVENTS.join(", ")}.

Rules:
- say: one friendly line, at most ${MAX_SAY} characters, about what you did. No questions unless you could do nothing.
- Never use brand names, real people, logos or anything from films, shows or games; make a generic version and name it generically. Nothing violent, sexual or hateful: then safety "decline", actions [], and a kind line.
- A mood or a feeling ("a rainy Sunday morning", "cosy") is a "world" action, sometimes with a "make" (a lantern, a cloud).
- If they ask for several things, use several actions. If they ask for something impossible here, do the nearest thing you can and say so.
- What is already in the room is listed; use "change" for "it", "that" or a name from the list.
- answer: null, except when the player is holding something up to look at it, or speaking to an animal in the world (either is named on "Holding up"), and asks a question about it ("how fast could it run?", "what does it eat?", "how heavy is it?"). Then answer the question in "answer", at most ${MAX_ANSWER} characters, in plain words a child can follow, with real, well-established facts about the real thing; if scientists aren't sure, say so; if it is imaginary, say it's imaginary and have fun with it. A question gets no actions (actions []), and say is a short lead-in ("Good question.").
- When a "Talking to" line names someone who lives in this world, the player is speaking to them. Reply in "answer" as that character, in the first person, at most ${MAX_ANSWER} characters, warm and a little playful, about themselves or this imaginary place (you may invent small harmless details that fit it). They never say they are an AI, never claim to be a real person, and never use brands or names from films, shows or games. No actions (actions []); say is a very short lead-in in their voice.

Examples:
<said>put a lantern on the table</said>
{"safety":"ok","say":"A lantern for your table.","answer":null,"actions":[{"do":"make","recipe":null,"name":"lantern","kind":"lantern","parts":null,"place":"table","count":1,"size":0.22,"target":null,"scale":null,"color":null,"remove":null,"event":null}]}
<said>make me a rubber duck</said>
{"safety":"ok","say":"One rubber duck, coming up.","answer":null,"actions":[{"do":"make","recipe":null,"name":"rubber duck","kind":null,"parts":["sphere 0 0.3 0 0.7 0.55 0.5 0 0 0 #f5c518","sphere 0.22 0.68 0 0.38 0.38 0.36 0 0 0 #f5c518","cone 0.44 0.66 0 0.16 0.2 0.14 0 0 -90 #f08a1c","cone -0.36 0.42 0 0.2 0.26 0.3 0 0 60 #f5c518","sphere 0.34 0.76 0.13 0.06 0.06 0.06 0 0 0 #1a1a1a","sphere 0.34 0.76 -0.13 0.06 0.06 0.06 0 0 0 #1a1a1a"],"place":"front","count":1,"size":0.25,"target":null,"scale":null,"color":null,"remove":null,"event":null}]}
<said>make it bigger and blue</said>
{"safety":"ok","say":"Bigger, and blue.","answer":null,"actions":[{"do":"change","recipe":null,"name":null,"kind":null,"parts":null,"place":null,"count":null,"size":null,"target":"last","scale":1.6,"color":"#3a7bd5","remove":null,"event":null}]}
Holding up: t rex
<said>how fast could it run?</said>
{"safety":"ok","say":"Good question.","answer":"Not very fast for its size. Scientists think a grown T. rex topped out around 20 km an hour, about as fast as a person running.","actions":[]}
Talking to: captain brine, a retired pirate captain, at pirate cove at sunset
<said>did you ever find treasure?</said>
{"safety":"ok","say":"Ha!","answer":"Found it twice, lost it three times. The map's under my hat, but don't tell the seagulls.","actions":[]}`;

/** The user turn: the world, what exists, and the player's words (angle brackets removed). */
export function actMessage(text, current, objects, places, holding = "", persona = "") {
  const things = objects.length ? objects.join(", ") : "nothing";
  const where = places.length ? places.join(", ") : "front, table, floor";
  const held = (holding ? `\nHolding up: ${holding}` : "") + (persona ? `\nTalking to: ${persona}` : "");
  return `Current world: ${current ? JSON.stringify(current) : "none"}\nAlready made: ${things}\nPlaces available: ${where}${held}\n<said>${String(text).replace(/[<>]/g, " ")}</said>`;
}

export function actRequest(model, text, current, objects, places, holding = "", persona = "") {
  return {
    model,
    max_tokens: 2600,
    thinking: { type: "disabled" },
    output_config: { effort: "low", format: { type: "json_schema", schema: ACT_SCHEMA } },
    system: [{ type: "text", text: ACT_PROMPT, cache_control: { type: "ephemeral" } }],
    messages: [{ role: "user", content: actMessage(text, current, objects, places, holding, persona) }],
  };
}

const num = (v, lo, hi, dflt) => (typeof v === "number" && Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : dflt);
const COLOR_RE = /^#[0-9a-fA-F]{6}$/;

/** One "shape x y z sx sy sz rx ry rz #rrggbb" line, clamped; null if it is not one. */
export function cleanPart(line) {
  if (typeof line !== "string" || line.length > 120) return null;
  const t = line.trim().split(/\s+/);
  if (t.length < 11 || !SHAPES.includes(t[0])) return null;
  const n = t.slice(1, 10).map(Number);
  if (n.some((v) => !Number.isFinite(v))) return null;
  if (!COLOR_RE.test(t[10])) return null;
  const c = (v, lo, hi) => Math.round(Math.min(hi, Math.max(lo, v)) * 1000) / 1000;
  const p = [c(n[0], -0.75, 0.75), c(n[1], -0.1, 1.25), c(n[2], -0.75, 0.75),
    c(n[3], 0.01, 1.5), c(n[4], 0.01, 1.5), c(n[5], 0.01, 1.5),
    c(n[6], -360, 360), c(n[7], -360, 360), c(n[8], -360, 360)];
  return `${t[0]} ${p.join(" ")} ${t[10].toLowerCase()}`;
}

/** A name the player will see: lower case letters, digits and spaces, checked by the caller's text filter. */
export function cleanName(v, ok) {
  if (typeof v !== "string") return "";
  const s = v.toLowerCase().replace(/[^a-z0-9 ]/g, " ").replace(/\s+/g, " ").trim().slice(0, MAX_NAME).trim();
  return s && ok(s) ? s : "";
}

/**
 * Turns whatever the model wrote into an answer the app can trust: known actions only, everything clamped,
 * names and the spoken line filtered (ok = the proxy's text filter), at most MAX_ACTIONS actions.
 */
export function finishAct(o, ok, current = null) {
  const declined = { safety: "decline", say: "Let's make something else.", actions: [] };
  if (!o || typeof o !== "object" || o.safety === "decline") return declined;
  const actions = [];
  for (const a of Array.isArray(o.actions) ? o.actions : []) {
    if (actions.length >= MAX_ACTIONS) break;
    if (!a || typeof a !== "object") continue;
    if (a.do === "world" && a.recipe && typeof a.recipe === "object") {
      const r = clampRecipe(a.recipe);
      if (!ok(r.title)) r.title = cleanTitle("");
      actions.push({ do: "world", recipe: r });
    } else if (a.do === "make") {
      const name = cleanName(a.name, ok) || (KINDS.includes(a.kind) ? a.kind.replace(/_/g, " ") : "");
      if (!name) continue;
      const kind = KINDS.includes(a.kind) ? a.kind : null;
      const parts = kind ? [] : (Array.isArray(a.parts) ? a.parts : []).map(cleanPart).filter(Boolean).slice(0, MAX_PARTS);
      if (!kind && parts.length === 0) continue;
      actions.push({
        do: "make", name, kind, parts,
        place: PLACES.includes(a.place) ? a.place : "front",
        count: Math.round(num(a.count, 1, MAX_COUNT, 1)),
        size: num(a.size, MIN_SIZE, MAX_SIZE, 0.25),
      });
    } else if (a.do === "change") {
      const target = cleanName(a.target, ok) || "last";
      const change = { do: "change", target };
      if (a.remove === true) change.remove = true;
      else if (typeof a.scale === "number" && Number.isFinite(a.scale)) change.scale = num(a.scale, 0.25, 4, 1);
      else if (typeof a.color === "string" && COLOR_RE.test(a.color)) change.color = a.color.toLowerCase();
      else continue;
      actions.push(change);
    } else if (a.do === "event" && EVENTS.includes(a.event)) {
      actions.push({ do: "event", event: a.event });
    }
  }
  let answer = typeof o.answer === "string" ? o.answer.replace(/\s+/g, " ").trim() : "";
  if (answer.length > MAX_ANSWER) {
    const cut = answer.slice(0, MAX_ANSWER);
    const end = Math.max(cut.lastIndexOf(". "), cut.lastIndexOf("! "), cut.lastIndexOf("? "));
    answer = end > 40 ? cut.slice(0, end + 1) : "";
  }
  if (answer && (!ok(answer) || /https?:|www\./i.test(answer))) answer = "";
  let say = typeof o.say === "string" ? o.say.replace(/\s+/g, " ").trim().slice(0, MAX_SAY) : "";
  if (!say || !ok(say)) say = actions.length ? "Here you go." : answer ? "Good question." : "I can't do that one. Try something else?";
  return answer ? { safety: "ok", say, answer, actions } : { safety: "ok", say, actions };
}

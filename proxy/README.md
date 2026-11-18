# Worldeck proxy

A small Cloudflare Worker for Worldeck, the Quest 3 mixed-reality app. The app understands common phrases offline
(`Words.cs`, `Wishes.cs`, the atlas) and sends only what it does not know to this relay, as text. Speech is recognised
on the headset; no audio ever comes here.

- `/v1/recipe` asks Claude Haiku 5.5 to choose a **recipe** from the app's fixed kit: one of 8 worlds plus a few knobs
  and, usually, a composed look.
- `/v1/act` asks it what to do about a request: make a thing, change one, change the world, or start an event, from a
  fixed list.

Every answer is clamped back into the kit before it leaves, and the app checks it again, so the app can only ever
build what we made. No npm dependencies: `worker.js` calls `https://api.anthropic.com/v1/messages` with plain `fetch`.

## Endpoints

| Call | Body | 200 reply |
|---|---|---|
| `POST /v1/recipe` | `{"text": "<≤ 300 chars>", "current": <recipe or null>, "kit": 1}` | `{"safety", "recipe", "clarify", "choices", "source", "ms"}` |
| `POST /v1/act` | `{"text", "kit": 1, "current", "objects", "places", "holding", "persona"}` | `{"safety", "actions", "say", "answer", "source", "ms"}` |
| `GET /health` | | `{"ok": true, "kit": 1}`; with the app key, also what is bound and switched on (below) |

- Both POSTs need `X-App-Key: <APP_KEY>` and `X-Device: <random install uuid>`.
- Every response carries `X-Request-Id`. The app keeps it in its play log, and the relay logs the same id, so one
  player's failed request can be found here.
- `source` is `seed` (hand-written, no model call), `cache` or `model`.
- Errors are `{"error": "..."}`. The app falls back to what it knows offline on any of them, and after a failure it
  stops asking for a while (`RelayGate.cs`) instead of making the player wait each time.
  - 400 for a bad body or headers;
  - 401 for a wrong key;
  - 429 for the rate limit, with `Retry-After`;
  - 502 when the model fails;
  - 503 when the model is switched off (`AI_OFF`), with `Retry-After: 600`;
  - 504 on timeout (9 s for `/v1/recipe`, 12 s for `/v1/act`).

## How a recipe request is answered

1. Check the key (401), the install id (400) and the rate limits: 12 per device and 300 overall per minute (429). The
   in-memory windows are per isolate; the Cloudflare Rate Limiting bindings in `wrangler.toml` are shared by all
   isolates in a location.
2. Validate the body (400).
3. Decline plainly off-limits words straight away, with no model call.
4. Look the text up in the **seed list** (`seeds.js`): 46 hand-written worlds under about 230 phrasings. Seeds work
   even with no API key, and while the model is switched off.
5. Check the in-isolate LRU, then KV (`RECIPES`, if bound). Both are keyed by a SHA-256 of the normalised text and the
   current world (the answer to "same but darker" depends on where you are).
6. Ask `claude-haiku-5-5` with a cached system prompt (`prompt.md`), structured outputs (a JSON schema whose enums are
   the kit), thinking disabled and effort `low`. Aborted at 9 s. A classifier refusal becomes a decline.
7. Clamp everything and trust nothing: unknown biomes map to the nearest world, unknown extras are dropped, numbers are
   clamped, and titles and questions are checked for profanity, brands, real people and links. A decline always gets
   the calm meadow world.

## Privacy stance

- **No transcripts are stored or logged.** What the player said exists only in memory while the request runs. Cache
  keys are SHA-256 hashes, never the text. Cached values are validated recipes.
- **No audio.** Speech is recognised on the headset; there is no speech endpoint.
- **One log line per request, with no player data**: the endpoint, status, total and model time, a random request id,
  token counts, the stop reason, the prompt version, the cache tier, and a code for why a request was refused or
  declined. Never the text, the key, the install id or any hash of it. The tests check this.
- The install id is a random UUID made by the app, never a Meta user id or a device serial. It is used only for the
  per-device limit, in memory, and forgotten after a minute.
- **Third party.** The text goes to Anthropic (Claude API) under its API terms.

## Observability

- **Logs.** `[observability]` is on in `wrangler.toml`: the one line per request is kept a few days by Workers Logs and
  can be searched in the Cloudflare dashboard (Workers, worldeck-proxy, Logs). `wrangler tail` shows it live. Useful
  fields: `status`, `err` (`model_http_429`, `model_timeout`, `model_bad_json`, `ai_off`, ...), `why` (`limit_device`,
  `limit_global`, `app_key`, ...), `ms` and `model_ms`, `tok` = [input, output, cache read, cache write], `tier`
  (`lru` or `kv`), `decline` (`pre`, `model`, `refusal`, `seed`), `pv` (which prompts were live), `rid`.
- **Health.** `GET /health` with `X-App-Key` returns the model, the prompt version, and whether the model key, the KV
  cache and both limiters are bound, and whether the model is switched off. Never a value.
- **Kill switch.** Set the plain var `AI_OFF = "1"` (dashboard, or `[vars]` in `wrangler.toml`, then deploy): no model
  calls at all; seeds still answer and the app carries on offline.
- **Spend.** Use a dedicated Anthropic workspace for this key with a monthly spend limit and an alert.

## Files

| File | What it is |
|---|---|
| `worker.js` | The Worker: routes, validation, caches, rate limits, the model call, the recipe prompt and schema, the log line. |
| `act.js` | `/v1/act`: its prompt, schema and the checks on its answer. |
| `kit.js` | The kit vocabulary, mirrored from `Kit.cs`, `Recipe.cs` and `Words.cs`. The tests compare it with the C# sources. |
| `seeds.js` | The 46 seed recipes, the calm decline world and the text normaliser used as the seed and cache key. |
| `places.js` | The composed looks of the app's offline places, for seeds that name them. |
| `prompt.md` | The recipe system prompt in readable form. It is generated from `worker.js`, and the tests fail if they differ. |
| `wrangler.toml` | Worker config: no secrets; rate-limit bindings and Workers Logs on; KV optional. |
| `test/*.test.js` | `node --test`, with fetch mocked; no network. |
| `scripts/write-prompt-md.js` | Rewrites `prompt.md` after a prompt, seed or kit change. |

## Test locally

```bash
cd "path/to/worldeck/proxy"
node --test
```

The suite covers schema clamping of hostile model output; seed, LRU and KV hits; 401, 400, 429 (device, global and the
binding), 502, 503 and 504; the Messages API request shape; the decline, refusal and pre-check paths; blanking of
titles and questions; the log line (fields, request id, no player data); `/health` with and without the key; the kill
switch; the prompt.md sync and the Kit.cs mirror. After editing the prompt, a seed or the kit, run
`node scripts/write-prompt-md.js`.

To check the config without deploying: `npx wrangler deploy --dry-run --outdir dist` lists the bindings it would use.

## Deploy (the owner's step)

You need a Cloudflare account (the free plan is enough) and an Anthropic API key with prepaid credit and a spend limit.
The Worker is already set up with its two secrets; after a code or config change, run:

```bash
cd "path/to/worldeck/proxy"
wrangler deploy
```

First-time setup, for reference: `wrangler login`, then `wrangler secret put ANTHROPIC_API_KEY` and
`wrangler secret put APP_KEY` (the same value as the app build's `X-App-Key`; make one with
`node -e "console.log(crypto.randomUUID() + crypto.randomUUID())"`), then `wrangler deploy`. Optional KV cache: follow
the comments in `wrangler.toml`.

### Smoke test

In Git Bash, set `BASE` to the Worker's URL and `KEY` to your APP_KEY:

```bash
BASE=https://worldeck-proxy.<your-subdomain>.workers.dev
KEY=<your APP_KEY>
DEV=11111111-2222-4333-8444-555555555555

curl -s "$BASE/health"
# {"ok":true,"kit":1}
curl -s "$BASE/health" -H "X-App-Key: $KEY"
# {"ok":true,"kit":1,"model":"claude-haiku-5-5","pv":"...","model_key":true,"ai_off":false,"kv":false,"device_limiter":true,"global_limiter":true}

# A seed: no model call
curl -s -X POST "$BASE/v1/recipe" -H "X-App-Key: $KEY" -H "X-Device: $DEV" \
  -H "Content-Type: application/json" -d '{"text":"a haunted house","current":null,"kit":1}'

# The model: run it twice; the second answer should say "source":"cache" (same isolate) 
curl -s -i -X POST "$BASE/v1/recipe" -H "X-App-Key: $KEY" -H "X-Device: $DEV" \
  -H "Content-Type: application/json" -d '{"text":"a lighthouse made of candy","current":null,"kit":1}'
# the X-Request-Id header is the rid in the log line

# Wrong key → 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/v1/recipe" -H "X-App-Key: nope" \
  -H "X-Device: $DEV" -H "Content-Type: application/json" -d '{"text":"a castle","current":null,"kit":1}'
```

In Windows PowerShell 5.1, `curl` is not real curl, so use this instead:

```powershell
$base = "https://worldeck-proxy.<your-subdomain>.workers.dev"
$h = @{ "X-App-Key" = "<your APP_KEY>"; "X-Device" = "11111111-2222-4333-8444-555555555555" }
Invoke-RestMethod "$base/health" -Headers @{ "X-App-Key" = $h["X-App-Key"] }
Invoke-RestMethod -Method Post -Uri "$base/v1/recipe" -Headers $h -ContentType "application/json" -Body '{"text":"a haunted house","current":null,"kit":1}'
```

## Expected cost and scale

Prices: Claude Haiku 5.5 at $0.10 per million input tokens and $0.50 per million output, cache reads $0.01 per
million and 5-minute cache writes $0.125 per million (from the claude-api skill's model table; check the
[pricing page](https://platform.claude.com/docs/en/about-claude/pricing) before budgeting). Token counts are estimates:
the recipe system prompt is about 28,000 characters (roughly 7,000-11,000 tokens), the act prompt about 7,300.

| Per 1,000 model calls | Cache warm | Cache cold |
|---|---|---|
| `/v1/recipe` (about 250 output tokens) | about $0.21 | about $1.25 |
| `/v1/act` (about 250 output tokens) | about $0.15 | about $0.45 |

Assuming 6 relay requests per player per day, 70% of them reaching the model (seeds, the atlas and the offline parser
answer the rest on the headset):

| Players a day | Requests | Rough cost | What gives first |
|---|---|---|---|
| 100 | about 600 | about $0.25 a day | Nothing on capacity. Watch the logs for `model_http_*` (a key or credit problem silently turns the AI off for everyone; the app falls back offline). |
| 1,000 | about 6,000 | about $1.30 a day | Anthropic input tokens per minute on a low usage tier at peaks; the in-isolate cache rarely hits (bind KV). |
| 10,000 | about 60,000 | about $9 a day | Cloudflare's free 100,000 requests a day is within 2×: move to Workers Paid ($5 a month). Tune `GLOBAL_LIMITER` to the Anthropic tier's limit. |

Cloudflare's free plan also limits CPU to 10 ms per request; this Worker measured about 3 ms per model-path request
offline. The shared app key only stops casual abuse (it can be pulled out of the APK); the real protection is the rate
limits plus the spend limit on the Anthropic key.

## Notes and choices

- **The kit lives in two places.** If `Kit.cs`, `Recipe.cs` or `Words.cs` change, update `kit.js`, then run
  `node scripts/write-prompt-md.js` and `node --test`. The mirror test fails until they agree.
- **Refusals.** A classifier refusal (`stop_reason: "refusal"`) is returned as a calm decline, not an error, and is
  not cached. Haiku 5.5 has no server-side fallback, so none is sent.
- Keep the Worker live throughout judging, and do not change its code after the submission deadline.

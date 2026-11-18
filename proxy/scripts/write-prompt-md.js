// Rewrites prompt.md from the system prompt embedded in worker.js. Run after editing the prompt,
// the seeds it quotes, or the kit:  node scripts/write-prompt-md.js
// The tests fail if prompt.md and worker.js disagree.

import { writeFileSync } from "node:fs";
import { SYSTEM_PROMPT, MODEL, PROMPT_EXAMPLES } from "../worker.js";

const md = `# Worldeck: the system prompt

This is the system prompt the proxy sends to \`${MODEL}\` for every place the app's offline parser
does not know. It is embedded in \`worker.js\` as \`SYSTEM_PROMPT\` and sent as one cached system block
(\`cache_control: ephemeral\`), so its bytes must not change between requests: no dates, ids or
per-request text in it.

How it fits together:

- The model answers with **structured outputs** (\`output_config.format\`, a JSON schema whose enums are
  the kit's 8 world ids, 4 precip values and 8 extras), thinking disabled, effort \`low\`.
- The worked examples are the proxy's own seed recipes (\`seeds.js\`), quoted through the same
  \`userMessage()\` format the live request uses, so examples and seeds cannot drift apart. The
  ${PROMPT_EXAMPLES.length} seeded examples are followed by an edit-the-current-world example, a clarify example
  and a decline example.
- The model is never trusted: whatever it returns is clamped by \`finish()\` in \`worker.js\` (unknown
  biome to the nearest by the kit's words, unknown extras dropped, numbers clamped, title and question
  checked for profanity, brands and people, decline to the calm world).
- The player's words arrive inside \`<said>\` tags with \`<\` and \`>\` removed, and the prompt tells the
  model they are a place description, never instructions.

Regenerate this file with \`node scripts/write-prompt-md.js\` after any change. The text between the
markers below is byte-for-byte what is sent.

<!-- prompt:start -->
\`\`\`\`text
${SYSTEM_PROMPT}
\`\`\`\`
<!-- prompt:end -->
`;

writeFileSync(new URL("../prompt.md", import.meta.url), md);
console.log(`prompt.md written (${SYSTEM_PROMPT.length} characters of prompt)`);

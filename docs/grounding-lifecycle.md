# Grounding lifecycle — the team playbook for create / update / delete / evaluate

This is the **baseline the NuGet package-grounding v-team uses** to create, change, and retire package
grounding (`AGENTS.md` files that ship in a package root). It is the operational "what to do, and when"
guide. The measurement rules it leans on — the ship gate, the terms, the harness — live in
[`grounding-eval-methodology.md`](./grounding-eval-methodology.md). Read that for the *how we measure*;
read this for the *which operation, and how to prove it*.

Core rule, inherited from the methodology: **a grounding change is a claim, and a claim ships with its
evidence.** Every operation below ends in a reproducible eval and a copy-paste card.

Worked exemplars (use these as templates):

| Operation | Exemplar | What it shows |
| --- | --- | --- |
| **CREATE** | [nuget-fetch#13](https://github.com/richlander/nuget-fetch/pull/13) | A new `AGENTS.md` graded BETTER on the mini tier (which needed it) and BETTER on frontier too — no regression anywhere. |
| **EVALUATE / UPDATE** | [markout grounding eval issue](https://github.com/richlander/markout/issues) | A 3 KB `AGENTS.md` measured against a real 18.8 KB package README to prove the curated file still earns its place. |

---

## 0. First question — does this package need grounding at all?

Grounding records **only what the model is proven to lack**, not model-resident knowledge. Before
authoring anything, find the package's **trap**: the wrong API, deprecated entrypoint, renamed type, or
non-obvious workflow a competent agent reaches for *without* the package in front of it.

Run the **baseline arm** (no grounding) over the 6-question unit. If the baseline already scores well
and never resorts to **archaeology** (web/cache rummaging to recover missing knowledge), the model
already knows this package — **do not author grounding**. Grounding is justified only by a measured gap.

---

## 1. CREATE — author new grounding

1. **Write `grounding/<unit>/AGENTS.md`.** Body ≤ `eng/agents-line-limit.txt` lines. Describe only the
   trap and the correct path; skip anything the model already knows. See
   [`authoring-principles.md`](./authoring-principles.md).
2. **Check the budget:** `grounding check-agents` validates every `AGENTS.md` is within the line
   limit. (SKILL.md is optional and maintainer-authored under `skills/<unit>/` — it is *not* generated;
   the harness synthesizes a transient wrapper at eval time and cleans it up.)
3. **Evaluate both tiers, n ≥ 3:**
   - **mini** (e.g. `claude-haiku-4.5`) — the tier that *needs* grounding.
   - **frontier** (e.g. `claude-opus-4.8`) — the tier that doesn't.
4. **Read the cards** (see [scoring.md](./scoring.md)). Each card grades grounding uniformly as **BETTER / NEUTRAL / WORSE** (same
   rubric for every model — the card grades, it doesn't decide). Apply the higher-level ship rule
   (see [scoring.md](./scoring.md)): **require BETTER on the mini tier** (it needs grounding) and **merely not-WORSE on
   the frontier tier** (a frontier BETTER is a welcome bonus). Ship only if both hold.
5. **Complete the README too (if the package has one).** The Brochure comparison is a **usability test
   of the README**: any question its arm fails, or archaeology it forces, is a README bug. Fix the README
   **in this PR**, using the finished `AGENTS.md` as the checklist of facts it must cover (human prose —
   not token-optimized; authoring-principles §2c), and re-run to confirm it also reaches success + 0
   archaeology. Author `AGENTS.md` *first*, then derive the README fixes from it — never mutate the big
   README to quality from scratch.
6. **Open the PR** using `.github/PULL_REQUEST_TEMPLATE.md`. Paste the four cards into *Metrics*, link
   this playbook and the methodology, and **commit the eval inputs so the package can keep its own
   loop**: the questions/prompts (the `eval.yaml` scenarios, linked from the PR) and the matched datasets
   (so the baseline can be reused via `--baseline-from`).

> **Environment hygiene (learned the hard way).** The baseline is *not* a clean ignorance control: a
> package's README/AGENTS.md are packed in its nupkg, so any `dotnet build` restores them to
> `~/.nuget/packages`, and any decompiler/inspector tool on the box (`dotnet-inspect`, ilspy, …) lets
> the baseline self-ground. Both *understate* grounding's value. Run evals on a box with **no global
> decompiler/inspector tools installed**, and treat every delta as a **lower bound** (the baseline's
> resourcefulness count is understated, so grounding's advantage is too).

---

## 2. UPDATE — change existing grounding

Trigger an update when the package's API changes, the README is rewritten, the model's resident
knowledge shifts, or a new trap appears. The operation is the same as CREATE plus one extra question:

- **Re-run the matched matrix** (mini + frontier, AGENTS + README).
- **Read the Brochure comparison** — a **usability test of the README** (Missing Manual vs Brochure, both via
  the grounding tool, baseline removed). Any question the README arm fails, or archaeology it forces, is a
  README bug to fix here too. Once the README is complete, AGENTS's remaining edge is efficiency/retrieval —
  if even that edge has vanished (the README now carries the same knowledge as cheaply), the curated file
  may no longer earn its place.
- Edit `AGENTS.md`, re-sync, re-eval, and open the PR with refreshed cards. The diff in the cards *is*
  the justification.

---

## 3. DELETE — retire grounding

Grounding is a liability once it is redundant (the model learned the package) or wrong (the API moved).
Deletion is also a claim and needs evidence:

- Re-run **baseline vs AGENTS** on both tiers. If the mini grade has **collapsed to NEUTRAL** — baseline now matches
  grounded on success/func with **resourcefulness already at ~0** (the model no longer needs to dig) — the grounding no longer pays for its tokens. Remove it.
- If the package is **retired/unsupported**, delete the grounding with a short note; no eval needed, but
  say so explicitly in the PR.
- Never silently delete: a removal PR carries the "collapsed to NEUTRAL" card or the retirement note.

---

## 4. EVALUATE — read the cards

All operations are decided off `grounding analyze`, which emits the copy-paste cards and the uniform
**BETTER / NEUTRAL / WORSE** grade. The grade model, the metric legend, the three cards, and the
tier-aware ship gate live in **[`scoring.md`](./scoring.md)**. The mini tier (haiku) is the primary
deliverable — the tier grounding exists to help; the frontier card is the no-harm safety check.

## 5. What every grounding PR contains

Same artifact list and reviewer checklist as [scoring.md](./scoring.md):

- `grounding/<unit>/AGENTS.md` within the line limit (`grounding check-agents`).
- Matched n ≥ 3 datasets for **both** tiers under `data/<unit>-6q/`.
- The four cards pasted into *Metrics*, matching the committed datasets.
- An **Analysis** of what grounding changes (typically eliminating the *resourcefulness* the agent spends
  to reach the **same** correct API — verify against the transcripts, not a guessed wrong-API story).
- **The eval inputs, so the package owns its loop going forward**: the questions/prompts (the `eval.yaml`
  scenarios) committed and **linked from the PR**, plus the matched datasets (for `--baseline-from` reuse).
- **README fixes** the eval surfaced, if the package has an extensive README (it must also reach success +
  0 archaeology — authoring-principles §2c).
- Required caveats: the cache self-grounding lower-bound, and cache-state-is-not-a-variable.

---

*See also: [`grounding-eval-methodology.md`](./grounding-eval-methodology.md) (the gate + terms),
[`harness.md`](./harness.md) (mechanics), [`authoring-principles.md`](./authoring-principles.md) (how to
write the body), [`delivery-and-retrieval.md`](./delivery-and-retrieval.md) (how grounding reaches the
agent).*

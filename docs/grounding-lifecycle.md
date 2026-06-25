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
2. **Sync the wrapper:** `eng/sync-skill.sh` regenerates `SKILL.md` (harness toggle only — never
   hand-edit it). `eng/sync-skill.sh --check` must pass.
3. **Evaluate both tiers, n ≥ 3:**
   - **mini** (e.g. `claude-haiku-4.5`) — the tier that *needs* grounding.
   - **frontier** (e.g. `claude-opus-4.8`) — the tier that doesn't.
4. **Read the cards** (§4). Each card grades grounding uniformly as **BETTER / NEUTRAL / WORSE** (same
   rubric for every model — the card grades, it doesn't decide). Apply the higher-level ship rule
   (methodology §3): **require BETTER on the mini tier** (it needs grounding) and **merely not-WORSE on
   the frontier tier** (a frontier BETTER is a welcome bonus). Ship only if both hold.
5. **Complete the README too (if the package has one).** The source-diff card (③) is a **usability test
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
- **Read the source-diff card** (③) — a **usability test of the README** (AGENTS.md vs README.md, both via
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

## 4. EVALUATE — the three cards

All operations are decided off `eng/analyze-6q.py`. It emits **three single-variable cards**, each
isolating exactly one comparison so the data is trivial to read. Every card shows the same metric rows
and a **Conclusion** — a single **uniform, model-independent grade** of grounding's effect vs baseline,
on **objective axes** (no judge-quality diff; see methodology §7). The same rubric grades every model;
the card grades, it does **not** decide shipping (that is §1 step 4 / methodology §3).

**Metric legend** — every card shows these rows, each read **per arm in isolation**:

| Row | Meaning | Better |
| --- | --- | :---: |
| **success (scenarios)** | A scenario is *solved* for an arm when every functional assertion passes **and** the judge's quality clears the **≥4 floor** ("meets expectations"). The 1–5 judge score is used *only* as this pass/fail floor — its subjective top band is discarded (methodology §7). | higher |
| **func passed (assertions)** | Build + file + run-output assertions met (target 100%) — the objective correctness signal inside `success`. | higher |
| **resourcefulness (archaeology)** | Out-of-sandbox lookups the agent had to make to recover the API: web fetch/search **+** local NuGet-cache rummaging / decompiling. Measured from the timeline, not the judge. Grounding's job is to drive it to **0**, so **lower is the win**. The **web** portion must be 0 in a grounded arm (hard guard). | lower |
| **IET** | Input-Equivalent Tokens = `(input − cache-reads) + output` — the cache-discounted token cost. | lower |
| **output tok** | Output / thinking tokens (priciest per token, most variable). | lower |
| **cost** | Premium-request multiplier (cache-discounted). | lower |

**Conclusion grade** — keyed off objective axes only:

| Grade | When |
| --- | --- |
| **BETTER** | success held **and** a real win — more scenarios solved, resourcefulness eliminated, or IET/cost cut ≥ 25%. |
| **NEUTRAL** | success held, no material efficiency win. |
| **WORSE** | success dropped, the grounded arm did open-web archaeology, or cost/IET/output inflated past the cap. |

**The three cards** — each isolates exactly one comparison:

| Card | Flag | Holds fixed | Varies | Answers |
| --- | --- | --- | --- | --- |
| ① **Primary** | `--card` | one model | baseline → AGENTS.md | Does grounding help *this* model? (one card per model, graded BETTER/NEUTRAL/WORSE) |
| ② **Model-diff** | `--model-diff` | AGENTS.md vs baseline | the model | Does the grade hold across tiers — side by side. |
| ③ **Source-diff** | `--source-diff` | one model, grounding-tool delivery | AGENTS.md vs README.md | A **usability test of the README** (not a floor to beat): does the README also answer every question with 0 archaeology? Its failures are bugs to fix in the same PR; once it's complete, AGENTS's edge is efficiency/retrieval. |

```bash
# ① primary, one card per model
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# ② model-diff (AGENTS lift, models side by side)
python3 eng/analyze-6q.py --model-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# ③ source-diff (AGENTS − README, one model)
python3 eng/analyze-6q.py --source-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>-readme.n3.haiku.json
```

A dataset whose filename contains `readme` is automatically read as the **README arm**. The mini tier
(haiku) is the primary deliverable — it is the tier grounding exists to help; the frontier card is the
safety check.

---

## 5. What every grounding PR contains

Same artifact list and reviewer checklist as methodology §5–§6:

- `grounding/<unit>/AGENTS.md` (within the line limit) + regenerated `SKILL.md` (`sync-skill.sh --check`).
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

_See also: [`grounding-eval-methodology.md`](./grounding-eval-methodology.md) (the gate + terms),
[`harness.md`](./harness.md) (mechanics), [`authoring-principles.md`](./authoring-principles.md) (how to
write the body), [`delivery-and-retrieval.md`](./delivery-and-retrieval.md) (how grounding reaches the
agent)._

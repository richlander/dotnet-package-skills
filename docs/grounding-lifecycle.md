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
| **CREATE** | [nuget-fetch#13](https://github.com/richlander/nuget-fetch/pull/13) | A new `AGENTS.md` that turns a mini-tier loss into a WIN with zero frontier harm. |
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
   - **mini** (e.g. `claude-haiku-4.5`) — the tier that *needs* grounding; you are looking for a **WIN**.
   - **frontier** (e.g. `claude-opus-4.8`) — the tier that doesn't; you are checking for **no HARM**.
4. **Read the cards** (§4). Ship only if the **mini WIN** gate and the **frontier no-harm check** (IET inflation under the cap) both pass
   (thresholds in methodology §3).
5. **Open the PR** using `.github/PULL_REQUEST_TEMPLATE.md`. Paste the four cards into *Metrics*, link
   this playbook and the methodology, and give one representative before/after.

> **Environment hygiene (learned the hard way).** The baseline is *not* a clean ignorance control: a
> package's README/AGENTS.md are packed in its nupkg, so any `dotnet build` restores them to
> `~/.nuget/packages`, and any decompiler/inspector tool on the box (`dotnet-inspect`, ilspy, …) lets
> the baseline self-ground. Both *understate* grounding's value. Run evals on a box with **no global
> decompiler/inspector tools installed**, and treat every quality delta as a **lower bound**.

---

## 2. UPDATE — change existing grounding

Trigger an update when the package's API changes, the README is rewritten, the model's resident
knowledge shifts, or a new trap appears. The operation is the same as CREATE plus one extra question:

- **Re-run the matched matrix** (mini + frontier, AGENTS + README).
- **Read the source-diff card** (③). It isolates `AGENTS.md − README.md` (both via the grounding tool,
  baseline removed). This is the test that the curated `AGENTS.md` still **beats the README floor** — if
  the README now carries the same knowledge, the curated file may no longer earn its place.
- Edit `AGENTS.md`, re-sync, re-eval, and open the PR with refreshed cards. The diff in the cards *is*
  the justification.

---

## 3. DELETE — retire grounding

Grounding is a liability once it is redundant (the model learned the package) or wrong (the API moved).
Deletion is also a claim and needs evidence:

- Re-run **baseline vs AGENTS** on both tiers. If the **mini WIN has collapsed** — baseline now matches
  grounded on quality/func with no archaeology — the grounding no longer pays for its tokens. Remove it.
- If the package is **retired/unsupported**, delete the grounding with a short note; no eval needed, but
  say so explicitly in the PR.
- Never silently delete: a removal PR carries the "WIN collapsed" card or the retirement note.

---

## 4. EVALUATE — the three cards

All operations are decided off `eng/analyze-6q.py`. It emits **three single-variable cards**, each
isolating exactly one comparison so the data is trivial to read. Each card shows the same metric rows —
`quality`, `func passed`, `IET`, `output tok`, `cost`, `archaeology` — and a **Conclusion** that is a
verdict *derived from* the rows, not a row itself.

| Card | Flag | Holds fixed | Varies | Answers |
| --- | --- | --- | --- | --- |
| ① **Primary** | `--card` | one model | baseline → AGENTS.md | Does grounding help *this* model? (one card per model; mini = WIN (required), frontier = no-harm (a win is welcome)) |
| ② **Model-diff** | `--model-diff` | AGENTS.md vs baseline | the model | Where does grounding's lift land — mini WIN vs frontier no-harm — side by side. |
| ③ **Source-diff** | `--source-diff` | one model, grounding-tool delivery | AGENTS.md vs README.md | Is authoring `AGENTS.md` worth it over the package README floor? |

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
- A representative before/after (the wrong API the ungrounded agent reaches for vs. what grounding makes
  it do).
- Required caveats: the cache self-grounding lower-bound, and cache-state-is-not-a-variable.

---

_See also: [`grounding-eval-methodology.md`](./grounding-eval-methodology.md) (the gate + terms),
[`harness.md`](./harness.md) (mechanics), [`authoring-principles.md`](./authoring-principles.md) (how to
write the body), [`delivery-and-retrieval.md`](./delivery-and-retrieval.md) (how grounding reaches the
agent)._

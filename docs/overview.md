# NuGet Package Grounding

Package grounding is a new concept (at least for .NET/NuGet) that is defined as package-resident (co-located) model context information. This information is intended to provide a model with authoritative information about how to use a library. NuGet MCP and `dotnet-inspect` have added support for offering this information to a model. The core question is how to decide which information should be included and how to determine if it helps or hurts. We've developed a process for evaluating grounding information that involves a sort of triangulation scheme across multiple files to generate confidence signal. This document describes that approach (at a high level).

Adding grounding information to the model makes it sound like its a singular consideration. Test the experience; if it's good you are done. That's not the case. The AI labs have a spectrum of models, mini to frontier, as categories. There are also routers which make selection and switching automatic, unpredictable, and black box. What works well for one model might not for another. Our approach is based on [Pareto improvement](https://en.wikipedia.org/wiki/Pareto_efficiency): make improvements to model usage, but do not harm to any one model. Harm is defined as worse outcomes. Eval is used to account for and keep that balance.

There are multiple markdown files that may be present in a repo, either intended (`AGENTS.md`, `SKILL.md`) or possibly not (`README.md`) as model context. They each have a separate audience, purpose, bias, benefit, and risk. A big part of how we define confidence signal and value is to take advantage of "eval diff" across these files. The rest of the document elaborates on that idea.

## Files

Let's start by defining what the files are for:

| File        | Nickname          | Goal      | Audience | Knowledge  | Acquisition  | Content |
| ----------- | ----------------- | --------- | -------- | ---------- | ------------ | ------- |
| `README.md` | Brochure        | Introduce | Humans   | Untrained  | Agent-driven | Marketing, intro, download instructions, progressive narrative + code on how to use the library. |
| `AGENTS.md` | Missing manual    | Gap-fill  | Models   | Trained    | Automatic    | RAG-style, reward-seeking, emergent, terse. |
| `SKILL.md`  | Complete textbook | Instruct  | Models   | Trained    | Opt-in       | (biasing to) Complete explanation of tool or workflow. Progressive if only to make easier to write and review |

Our starting assumption is that a given repo has a `README.md` and `AGENTS.md` (published in-package) and optionally has a `SKILL.md`. `SKILL.md` can mean multiple things. We're ignoring that; it doesn't matter. Some repos have a repo `README.md` and package `README.md` as separate files and sometimes they are one in the same. This distinction also isn't important for our analysis. And for repos that have none of these files, none of this analysis applies.

There are two ways to think about `AGENTS.md` relative to `README.md`:

- We provide `AGENTS.md` instead of `README.md` because the former has been specifically crafted for the purpose and achieves better metrics as a result.
- We provide `AGENTS.md` as protection over a comparatively large and unruly `README.md`.

Note: Many Microsoft packages ship with `PACKAGE.md`. That file is treated as equivalent to `README.md`.

## Knowledge

> The critical question with grounding: "does the user need this information; do they already know this about the package?".

We split knowledge into "trained" and "untrained" with a gradient decay.

- Models: trained on popular packages and untrained for niche ones, with a decay curve that (one guesses) roughly tracks blog post and Stack Overflow volume, which closely follows the popularity curve. The difference between frontier and mini is likely that the knowledge decay curve starts to decay sooner, but the most popular packages are still present in model understanding.
- Humans: specific developers are trained on concept and syntax for some packages but not all; others are trained on concept but not syntax for some packages, and most  developers are untrained on most or all packages. Humans as a group bias to untrained on all packages.

Our concept of knowledge doesn't include web search or tools. That's different.

## Content

The files are intended to differ significantly, hinted at via the columns in the table. The three styles require more explanation. I'll continue to use the file names as the identifier even though the style could apply to other files.

- `README.md` -- This is the _project brochure that gets users engaged_ on capability and first adoption steps. It is generally a human-facing front door but can provide agent-value (beyond model resident knowledge) if the package is niche. The writing style is largely patterned off repo READMEs that the author likes best or time-boxed to the time they have. Can be agent-generated.
- `AGENTS.md` -- Solely intended to _fill model gaps_ with no affordance for natural human consumption. Human users don't ever see this document in any normal workflow. Creating the document really only works as an _emergent phenomenon of eval_, starting with an empty doc. It cannot be apriori agent-generated. An agent generating grounding for other agents is circular and only serves to demonstrate that the grounding is model-resident and therefore not needed. In theory, we could use Opus to generate grounding for Haiku as a sort of distillation, but this approach is a dull knife because we don't know what Haiku gaps are or what the harm will be in Opus reading grounding that is by definition model-resident (for it). 
- `SKILL.md` -- This is _the workflow-oriented instruction manual_, what users reach for after making an adoption decision (possibly after reading the brochure). This document is intended for agents but needs more of a human touch than `AGENTS.md`. The reason is that `SKILL.md` are opt-in, requiring a gesture to adopt via `plugin.json` or to vendor into a consuming repo. Skills are typically less of a "man page" and more likely to describe useful workflows and possibly prescribe the use of multiple tools. It is a lot more risky for `AGENTS.md` to suggest using extra tools, which they have no ability to reason about the consuming environment. Humans can and do read `SKILL.md` so there is a risk of making it as choppy and RAG-centric as is intended for `AGENTS.md`.

## Task Ladder

We start from the idea that every package has a `README.md`. It is the default grounding content. That means that `AGENTS.md` has to better than it to pay its way, and by a decent premium. If not, why bother maintaining two documents? `SKILL.md` isn't in the same continuum and we don't encourage including it in NuGet packages. However, it can be used as an excellent evaluation comparison because it is (in theory) at the limit of package knowledge and should be able to satisfy any eval test question with ease.

We use a three-tier task ladder that maps to the three documents.

- **Core (Core-6)** -- the most 6 basic/common task questions
- **Missing Manual (MM-12)** -- the +6 most valuable model-gap task questions
- **Complete Textbook (CT-24)** -- the +12 most valuable advanced task questions

Note: The task questions are human- or agent-generated/reviewed questions that are intended to range from what you'd need to do on day 1 through day 100. The questions get more niche as you get to question 24.

We then perform a number of H2H comparisons using the task ladder as the task, against baseline (no grounding) and each of the documents, with both mini and frontier model.

This process is attempting to disentangle three dimensions:

- What is the relative performance of each document, per question tier?
- How do the documents generalize across question tiers?
- Is Pareto maintained across models.

And the side-benefit is that by virtue of using these three documents to triangulate value/harm for `AGENTS.md` that we get eval benefits for all three.

## Grounding effectiveness

> This template is taken from [docs/templates/canonical-grounding-pr.md](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/templates/canonical-grounding-pr.md).

_Each cell: baseline (no grounding) → `AGENTS.md` (~904 tok). Columns are models. Judge `claude-haiku-4.5`. Means across scenarios._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| tasks correct (+) | 5/6 → 6/6 | 6/6 → 6/6 |
| func passed (assertions) (+) | 17/18 → 18/18 | 18/18 → 18/18 |
| resourcefulness (archaeology) (-) | 23 → 1 | 21 → 0 |
| IET (-) | 29664 → 17702 | 29930 → 28515 |
| output tok (-) | 5543 → 1704 | 5226 → 1408 |
| cost (-) | 7.23 → 2.30 | 16.40 → 5.12 |
| **verdict** | **BETTER** | **BETTER** |

_**FAIL** = fewer tasks correct; **BETTER** = more tasks correct / archaeology→0 / IET/cost cut ≥20%; **WORSE** = IET/cost/output inflated ≥20%; **NEUTRAL** = held. Archaeology, web, judge are signals, not gates._


Here's a quick pass on the rows:

- **tasks correct (+)**: The number of tasks answered correctly.
- **func passed (assertions) (+)**: The number of functional assertions passed (this is how eval tests for correct and incorrect answers).
- **resourcefulness (archaeology) (-)**: The number of web and file system searches looking for insight. The web searches might end up on GitHub looking for the package repo. The file system searches might end up looking for the package in the nuget cache and then reverse engineering the libraries it finds in various ways.
- **IET (-)**: This is "Input Equivalent Tokens": `input + 0.1 * cache + 5 * output`. It gives you a single number that preserves the price spread.
- **output tok (-)**: This metric captures just the expensive tier (5x input, 50x cache)?
- **cost (-)**: This is the dollar cost. It is equivalent to: `IET * input_cost`. 

Each row carries the desired up or down direction.

The table demonstrates what we're measuring. There are two dimensions in that table:

- Model A vs B
- Baseline vs grounding

This two dimensional aspect provides us with a wide field of view. Our goal is to drive perfect scores on the first three rows for `README.md` and `AGENTS.md` and premium value for `AGENTS.md` on the remaining rows. More generally, we're doing eval so (A) why leave `README.md` in a poor state, and (B) `AGENTS.md` benefits from fierce competition (baseline and `README.md`). This is a raise all boats exercise. Also, it's entirely possible that training is based (in part) on `README.md`. A good/better `README.md` will be a better target for next-round AI lab training, putting even more pressure on `AGENTS.md` in the future.

## Eval process

The tests answer a set of questions and drive quality work.

### `AGENTS.md` Creation

We start by using eval as a generative process. We start with only baseline eval on Core-6. This is how we identify what the gaps are. `AGENTS.md` is intended to be an emergent result of _probing the model_ with our task questions. If we get back perfect scores, then `AGENTS.md` isn't needed. Assuming non-perfect scores, we add content to `AGENTS.md` that improves scores, reduces archeology, or that lowers thinking. This is an iterative process until we hit steady state. We then enter the gauntlet of the H2H eval.

### Test: Core-6 H2H

>Theme: How effective are these documents at providing insight to the model?

Eval using the Core-6 questions:

- Baseline vs `README.md`
- Baseline vs `AGENTS.md`

Questions to answer:

- Does `README.md` answer the most basic questions a user might have?
- Does `AGENTS.md` pay it's way? 

Quality work:

- Update `AGENTS.md` and `README.md` until they can be used to correctly answer this broader set of questions.
- Update `AGENTS.md` until it provides premium value over `README.md`.

Non goal:

- Update `README.md` to be economic (low tokens/cost).

### Test: MM-12 H2H

> Theme: How well does `AGENTS.md` generalize?

Eval using the MM-12 questions:

- Baseline vs `AGENTS.md`

Questions to answer:

- Does `AGENTS.md` generalize to a broader set of questions?

Quality work:

- Update `AGENTS.md` until it can be used to correctly answer all questions.

Non-Goal:

- Update `README.md` until it answers this broader set of questions. We don't update it unless there is a desire to make `README.md` more effective for its audience. The process itself does not call for that.

Note: There is a desire to avoid overfitting to the questions. In favorable cases, we've found that content hits 11/12 on MM-12 on first run. We then just address that last question and call it good. If the first MM-12 run results in a 6/12 score, that likely means that `AGENTS.md` is overfit to Core-6 or the tasks in Core-6 and the MM-12 +6 are in disjoint domains. This latter problem is likely a task design problem. The tasks should grow in complexity not switch domains. Otherwise, there is no connective tissue between the ladder rungs and the value that they provide is significantly lessened.

### Test: CT-24 H2H

Eval using the CT-24 questions:

- `SKILL.md` vs `AGENTS.md`

Questions:

- Does `SKILL.md` answer a broad set of questions that user might have?
- Does `SKILL.md` earn a premium for the (presumably for larger input token cost) over `AGENTS.md`?
- Does `AGENTS.md` generalize to a broader set of questions?

Quality work:

- Update `SKILL.md` until it can be used to correctly answer all questions.

Non-goal:

- Update `AGENTS.md` and `README.md` until they can be used to correctly answer this broader set of questions.

There isn't a specific change that `AGENTS.md` needs to make here. The generalization signal is something that can be acts on or not.

### `AGENTS.md` Compression

`AGENTS.md` is intended as low-cost knowledge. We started with an empty file in order to discover the model knowledge gap. We now have the opportunity to repeat the exercise but in the other direction. How much can be compress or minify `AGENTS.md` without losing performance. In theory, one can imagine cutting the file in half to a set of facts that overlay on top of trained knowledge.

There is no specific instruction here. It's really just a reminder that you can use much the same process to compress `AGENTS.md` for improved performance (on eval rows) as was used to create it.

## Maintenance

Nothing sits in one place for long in the software or AI industries. It is best to run eval regularly and when new models are released. The numbers will definitely change. In the ideal case, the model pushes `AGENTS.md` further up the complexity ladder. It is a good thing if the model can answer questions 1-12 without help and `AGENTS.md` focused on more niche topics, if that's considered valuable.

The proposed task ladder tops out at 24 questions. There is no specific reason why there is that limit. If needed, more questions can be added. This doubling scheme was seen as a useful approach, generally, and to map to the brochure, missing manual, and complete textbook paradigm that was adopted.

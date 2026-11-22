# Learning Projects: How We Work

Status: v0.1, 2026-11-22. How every learning project under `docs/learning/`
is done. Claude writes the code, but in **hand-holding mode**: the idea
always comes first, the code comes in small pieces, and nothing moves on
until the developer is ready.

## Why this exists

The first attempt at reliable-udp step 01 went wrong twice (2026-11-22):

1. Claude built the whole step (echo server and client, tests, findings) in
   one go. Nothing was learned: the result was handed over finished.
2. The restart wrote the first piece of code *first* and explained it
   afterwards, between tool calls. Code before idea teaches nothing either.

All code from that attempt was thrown away and the step starts over with
the method below.

## The loop, for every piece

1. **Idea first, no code.** Explain in plain words what has to happen and
   why: an analogy if it helps, the web-world parallel, the terms involved.
   No code in this part.
2. **Questions.** The developer asks until the idea is clear. Claude waits.
3. **Small code piece.** Only a few lines, written only after the idea is
   understood. Each line is tied back to the idea it implements.
4. **Run it together.** Run it, look at the output, explain what we see and
   anything surprising.
5. **Try and ask.** Suggestions for what to try by hand. Claude waits for
   the developer's go-ahead before the next piece.

## Rules

- **Never** write ahead: no finished steps, no several pieces at once, no
  "here's the rest".
- **Never** code before the idea, and never the explanation only after
  the code.
- Explanations go in the conversation text, in one readable message, not
  scattered between tool calls.
- Each piece is small enough to read in a minute.
- **Findings** in a step doc are what we observed together while doing it,
  written after we've seen it, never in advance.
- If something goes faster or slower than planned, the developer decides
  the pace.

A short version of these rules is in `.claude/rules/learning-projects.md`,
which is loaded into every Claude session; keep the two in sync.

## Layout

- Docs: `docs/learning/<project>/`: README.md (why, steps with status) plus
  one doc per step (learn, build, test, findings).
- Code: `learning/<project>/` at the repo root, created piece by piece as
  the steps go, added to the root `BulletHeavenLike.slnx` when a project is
  first created.

## Projects

- [reliable-udp/](reliable-udp/README.md): own network transport over UDP.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.1 | Method written after the failed first attempt at reliable-udp step 01: idea first, small pieces, run together, wait for go-ahead. |
